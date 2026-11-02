using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Moonad;
using TradeSimulator.Account.Common;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.Enums;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Features.Shared;

namespace TradeSimulator.Account.Processors.OrderFunds;

public sealed class OrderFundsProcessor(
    IOrderFundsStore store,
    IAccountStore accounts,
    IFundsGate gate,
    IOfferBookClient offerBook,
    IAccountMetrics metrics,
    ILogger<OrderFundsProcessor> logger)
{
    public const int MaxFillAttempts = 5;

    private readonly ConcurrentDictionary<Guid, int> _fillAttempts = new();

    // Synchronous reservation. Business failures come back as a result; only infrastructure failures throw.
    // The order id doubles as the envelope id, so repeating the call for the same order is idempotent.
    public async Task<FundsReservationResult> ReserveAsync(OrderSubmission submission, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = await ReserveCoreAsync(submission, cancellationToken);
        metrics.ReservationCompleted(
            result.Reserved ? "reserved" : "rejected_" + result.Kind, System.Diagnostics.Stopwatch.GetElapsedTime(started));
        return result;
    }

    private async Task<FundsReservationResult> ReserveCoreAsync(OrderSubmission submission, CancellationToken cancellationToken)
    {
        var orderId = submission.OrderId;
        if (submission.Side != OrderSide.Buy)
            return FundsReservationResult.Failure(FundsReservationErrorKind.InvalidOrder);

        var amount = await ComputeAmountAsync(submission, cancellationToken);
        if (amount.IsError)
            return await RejectAsync(submission, amount.ErrorValue, cancellationToken);

        var account = AccountId.TryCreate(submission.AccountId);
        if (account.IsError)
            return await RejectAsync(submission, account.ErrorValue, cancellationToken);

        var cents = (long)Math.Round(amount.ResultValue * 100m, MidpointRounding.AwayFromZero);
        var admitted = await gate.TryReserveAsync(account.ResultValue, orderId, cents, cancellationToken);
        if (admitted == FundsGateResult.Miss)
        {
            var loaded = await accounts.GetAsync(account.ResultValue, cancellationToken);
            if (loaded.IsError)
                return await RejectAsync(submission, loaded.ErrorValue, cancellationToken);

            await gate.HydrateAsync(
                account.ResultValue, (long)Math.Round(loaded.ResultValue.AvailableBalance * 100m, MidpointRounding.AwayFromZero), cancellationToken);
            admitted = await gate.TryReserveAsync(account.ResultValue, orderId, cents, cancellationToken);
        }

        if (admitted == FundsGateResult.Insufficient)
        {
            await store.RejectAsync(orderId.Guid, orderId, "ReservationExceedsAvailableBalance", null, cancellationToken);
            return FundsReservationResult.Failure(FundsReservationErrorKind.InsufficientFunds);
        }

        if (admitted != FundsGateResult.Reserved)
            throw new InvalidOperationException($"Funds gate unavailable for order {orderId.Guid}.");

        Result<OrderFundsOutcome, IAccountError> reserved;
        try
        {
            reserved = await store.ReserveAsync(orderId.Guid, orderId, account.ResultValue, amount.ResultValue, cancellationToken, notify: false);
        }
        catch
        {
            await gate.CompensateAsync(account.ResultValue, orderId, CancellationToken.None);
            throw;
        }

        if (reserved.IsError)
        {
            await gate.CompensateAsync(account.ResultValue, orderId, CancellationToken.None);
            return await RejectAsync(submission, reserved.ErrorValue, cancellationToken);
        }

        logger.LogInformation(
            "Order {OrderId}: reservation of {Amount} for account {AccountId} ({Outcome}).",
            orderId.Guid, amount.ResultValue, submission.AccountId, reserved.ResultValue);
        return FundsReservationResult.Success(amount.ResultValue);
    }

    // Reverts a reservation made by ReserveAsync (order cancelled, or the submission failed after the reservation).
    public async Task ReleaseAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        var result = await store.ReleaseAsync(Guid.CreateVersion7(), orderId, cancellationToken);
        if (result.IsError)
            throw new InvalidOperationException($"Could not release order {orderId.Guid}: {result.ErrorValue.GetType().Name}.");
    }

    public async Task SettleFillAsync(
        Guid envelopeId,
        OrderId orderId,
        long executedQuantity,
        decimal price,
        bool final,
        CancellationToken cancellationToken)
    {
        var eventType = final ? "OrderFilled" : "OrderPartiallyFilled";
        var type = final ? "fill" : "partial_fill";
        var amount = OrderFundsCalculator.ForFill(executedQuantity, price);
        if (amount.IsError)
        {
            logger.LogWarning("{EventType} {EnvelopeId} has an invalid quantity or price; discarded.", eventType, envelopeId);
            metrics.SettlementCompleted(type, "invalid");
            return;
        }

        var result = await store.DebitAsync(envelopeId, orderId, amount.ResultValue, final, cancellationToken);
        Complete(eventType, type, envelopeId, orderId.Guid, result);
    }

    public async Task SettleClosureAsync(Guid envelopeId, OrderId orderId, CancellationToken cancellationToken)
    {
        var result = await store.ReleaseAsync(envelopeId, orderId, cancellationToken);
        Complete("OrderClosure", "closure", envelopeId, orderId.Guid, result);
    }

    private void Complete(string eventType, string type, Guid envelopeId, Guid orderId, Result<OrderFundsOutcome, IAccountError> result)
    {
        if (result.IsError)
        {
            metrics.SettlementCompleted(type, "failed");
            // A rule violation on settlement is not retryable data; infrastructure/state errors are the same signal for redelivery.
            throw new InvalidOperationException(
                $"Could not settle {eventType} {envelopeId} of order {orderId}: {result.ErrorValue.GetType().Name}.");
        }

        if (result.ResultValue != OrderFundsOutcome.UnknownOrder)
        {
            _fillAttempts.TryRemove(envelopeId, out _);
            metrics.SettlementCompleted(type, result.ResultValue.ToString().ToLowerInvariant());
            logger.LogInformation("{EventType} of order {OrderId} settled ({Outcome}).", eventType, orderId, result.ResultValue);
            return;
        }

        // The reservation may still be in flight: redeliver a few times, then give up.
        var attempts = _fillAttempts.AddOrUpdate(envelopeId, 1, (_, current) => current + 1);
        if (attempts >= MaxFillAttempts)
        {
            _fillAttempts.TryRemove(envelopeId, out _);
            metrics.SettlementAbandoned(type);
            logger.LogWarning(
                "{EventType} {EnvelopeId}: no reservation for order {OrderId} after {Attempts} attempts; discarded.",
                eventType, envelopeId, orderId, attempts);
            return;
        }

        metrics.SettlementCompleted(type, "retry");
        throw new InvalidOperationException($"Reservation of order {orderId} is not available yet (attempt {attempts}).");
    }

    private async Task<Result<decimal, IAccountError>> ComputeAmountAsync(OrderSubmission submission, CancellationToken cancellationToken)
    {
        if (submission.Quantity <= 0)
            return Result<decimal, IAccountError>.Error(new InvalidReservationAmount());

        switch (submission.Type)
        {
            case OrderType.Limit when submission.Price is { } price:
                return OrderFundsCalculator.ForLimit(submission.Quantity, price);
            case OrderType.Market when !string.IsNullOrWhiteSpace(submission.Ticker):
                var ask = await offerBook.GetBestAskAsync(submission.Ticker.Trim().ToUpperInvariant(), cancellationToken);
                return ask.IsError
                    ? Result<decimal, IAccountError>.Error(ask.ErrorValue)
                    : OrderFundsCalculator.ForMarket(submission.Quantity, ask.ResultValue);
            default:
                return Result<decimal, IAccountError>.Error(new InvalidReservationAmount());
        }
    }

    private async Task<FundsReservationResult> RejectAsync(OrderSubmission submission, IAccountError error, CancellationToken cancellationToken)
    {
        var kind = error switch
        {
            ReservationExceedsAvailableBalance => FundsReservationErrorKind.InsufficientFunds,
            AccountNotFound => FundsReservationErrorKind.AccountNotFound,
            PriceUnavailable => FundsReservationErrorKind.PriceUnavailable,
            InvalidAccountId or InvalidOrderId or InvalidReservationAmount or AccountBalanceLimitExceeded =>
                FundsReservationErrorKind.InvalidOrder,
            _ => (FundsReservationErrorKind?)null
        };

        if (kind is null)
            throw new InvalidOperationException($"Could not reserve funds of order {submission.OrderId.Guid}: {error.GetType().Name}.");

        logger.LogWarning("Order {OrderId}: reservation failed ({Kind}).", submission.OrderId.Guid, kind);
        await store.RejectAsync(submission.OrderId.Guid, submission.OrderId, kind.Value.ToString(), null, cancellationToken);
        return FundsReservationResult.Failure(kind.Value);
    }
}
