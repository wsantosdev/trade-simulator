using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Moonad;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.Enums;
using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Processors.OrderFunds;

public sealed class OrderFundsProcessor(
    IOrderFundsStore store,
    IOfferBookClient offerBook,
    ILogger<OrderFundsProcessor> logger)
{
    public const int MaxFillAttempts = 5;

    private readonly ConcurrentDictionary<Guid, int> _fillAttempts = new();

    // Throws only on infrastructure failures (the message must be redelivered).
    public async Task ReserveAsync(Guid envelopeId, OrderSubmission submission, CancellationToken cancellationToken)
    {
        var orderId = submission.OrderId;
        if (submission.Side != OrderSide.Buy)
        {
            var ignored = await store.RejectAsync(envelopeId, orderId, "NotABuyOrder", null, cancellationToken);
            logger.LogDebug("Discarded non-buy order {OrderId} ({Outcome}).", orderId.Guid, ignored);
            return;
        }

        var amount = await ComputeAmountAsync(submission, cancellationToken);
        if (amount.IsError)
        {
            await RejectAsync(envelopeId, submission, amount.ErrorValue, cancellationToken);
            return;
        }

        var account = AccountId.TryCreate(submission.AccountId);
        if (account.IsError)
        {
            await RejectAsync(envelopeId, submission, account.ErrorValue, cancellationToken);
            return;
        }

        var reserved = await store.ReserveAsync(envelopeId, orderId, account.ResultValue, amount.ResultValue, cancellationToken);
        if (reserved.IsError)
        {
            await RejectAsync(envelopeId, submission, reserved.ErrorValue, cancellationToken);
            return;
        }

        logger.LogInformation(
            "Order {OrderId}: reservation of {Amount} for account {AccountId} ({Outcome}).",
            orderId.Guid, amount.ResultValue, submission.AccountId, reserved.ResultValue);
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
        var amount = OrderFundsCalculator.ForFill(executedQuantity, price);
        if (amount.IsError)
        {
            logger.LogWarning("{EventType} {EnvelopeId} has an invalid quantity or price; discarded.", eventType, envelopeId);
            return;
        }

        var result = await store.DebitAsync(envelopeId, orderId, amount.ResultValue, final, cancellationToken);
        Complete(eventType, envelopeId, orderId.Guid, result);
    }

    public async Task SettleClosureAsync(Guid envelopeId, OrderId orderId, CancellationToken cancellationToken)
    {
        var result = await store.ReleaseAsync(envelopeId, orderId, cancellationToken);
        Complete("OrderClosure", envelopeId, orderId.Guid, result);
    }

    private void Complete(string eventType, Guid envelopeId, Guid orderId, Result<OrderFundsOutcome, IAccountError> result)
    {
        if (result.IsError)
        {
            // A rule violation on settlement is not retryable data; infrastructure/state errors are the same signal for redelivery.
            throw new InvalidOperationException(
                $"Could not settle {eventType} {envelopeId} of order {orderId}: {result.ErrorValue.GetType().Name}.");
        }

        if (result.ResultValue != OrderFundsOutcome.UnknownOrder)
        {
            _fillAttempts.TryRemove(envelopeId, out _);
            logger.LogInformation("{EventType} of order {OrderId} settled ({Outcome}).", eventType, orderId, result.ResultValue);
            return;
        }

        // The reservation may still be in flight: redeliver a few times, then give up.
        var attempts = _fillAttempts.AddOrUpdate(envelopeId, 1, (_, current) => current + 1);
        if (attempts >= MaxFillAttempts)
        {
            _fillAttempts.TryRemove(envelopeId, out _);
            logger.LogWarning(
                "{EventType} {EnvelopeId}: no reservation for order {OrderId} after {Attempts} attempts; discarded.",
                eventType, envelopeId, orderId, attempts);
            return;
        }

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

    private async Task RejectAsync(Guid envelopeId, OrderSubmission submission, IAccountError error, CancellationToken cancellationToken)
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
        var failure = new FundsReservationFailure(submission.OrderId.Guid, submission.AccountId, kind.Value, error.GetType().Name);
        await store.RejectAsync(envelopeId, submission.OrderId, kind.Value.ToString(), failure, cancellationToken);
    }
}
