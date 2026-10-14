using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Application;

public sealed class OrderFundsProcessor(
    IOrderFundsStore store,
    IOfferBookClient offerBook,
    ILogger<OrderFundsProcessor> logger)
{
    public const int MaxFillAttempts = 5;

    private readonly ConcurrentDictionary<Guid, int> _fillAttempts = new();

    // Throws only on infrastructure failures (the message must be redelivered).
    public async Task ProcessOrderSubmittedAsync(string body, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var envelope = ReadEnvelope(body, options);
        if (envelope is null || envelope.EventType != FundsEventTypes.OrderSubmitted)
            return;

        OrderSubmittedMessage? message;
        try
        {
            message = envelope.Event.Deserialize<OrderSubmittedMessage>(options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "OrderSubmitted {EnvelopeId} has an invalid payload; discarded.", envelope.Id);
            return;
        }

        var orderId = message is null ? default : OrderId.TryCreate(message.OrderId);
        if (message is null || orderId.IsError)
        {
            logger.LogWarning("OrderSubmitted {EnvelopeId} has no valid order id; discarded.", envelope.Id);
            return;
        }

        if (message.Side != OrderSide.Buy)
        {
            var ignored = await store.RejectAsync(envelope.Id, orderId.ResultValue, "NotABuyOrder", null, cancellationToken);
            logger.LogDebug("Discarded non-buy order {OrderId} ({Outcome}).", message.OrderId, ignored);
            return;
        }

        var amount = await ComputeAmountAsync(message, cancellationToken);
        if (amount.IsError)
        {
            await RejectAsync(envelope.Id, orderId.ResultValue, message, amount.ErrorValue, cancellationToken);
            return;
        }

        var account = AccountId.TryCreate(message.AccountId);
        if (account.IsError)
        {
            await RejectAsync(envelope.Id, orderId.ResultValue, message, account.ErrorValue, cancellationToken);
            return;
        }

        var success = new FundsReservationResponse(
            Guid.CreateVersion7(),
            FundsEventTypes.ReservationResult,
            Result<FundsReservedMessage, FundsReservationError>.Ok(
                new FundsReservedMessage(message.OrderId, message.AccountId, amount.ResultValue)));

        var reserved = await store.ReserveAsync(
            envelope.Id, orderId.ResultValue, account.ResultValue, amount.ResultValue, success, cancellationToken);
        if (reserved.IsError)
        {
            await RejectAsync(envelope.Id, orderId.ResultValue, message, reserved.ErrorValue, cancellationToken);
            return;
        }

        logger.LogInformation(
            "Order {OrderId}: reservation of {Amount} for account {AccountId} ({Outcome}).",
            message.OrderId, amount.ResultValue, message.AccountId, reserved.ResultValue);
    }

    public async Task ProcessExchangeEventAsync(string body, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var envelope = ReadEnvelope(body, options);
        if (envelope is null)
            return;

        switch (envelope.EventType)
        {
            case FundsEventTypes.OrderPartiallyFilled:
            case FundsEventTypes.OrderFilled:
                await ProcessFillAsync(envelope, options, cancellationToken);
                break;
            case FundsEventTypes.OrderCancelled:
            case FundsEventTypes.OrderRejected:
                await ProcessClosureAsync(envelope, options, cancellationToken);
                break;
            default:
                logger.LogDebug("Discarded exchange event {EventType}.", envelope.EventType);
                break;
        }
    }

    private async Task ProcessFillAsync(EventEnvelope<JsonElement> envelope, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var message = Read<OrderFillMessage>(envelope, options);
        var orderId = message is null ? default : OrderId.TryCreate(message.OrderId);
        var price = message?.Price ?? message?.ExecutionPrice;
        if (message is null || orderId.IsError || price is null)
        {
            logger.LogWarning("{EventType} {EnvelopeId} is invalid; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        var amount = OrderFundsCalculator.ForFill(message.ExecutedQuantity, price.Value);
        if (amount.IsError)
        {
            logger.LogWarning("{EventType} {EnvelopeId} has an invalid quantity or price; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        var result = await store.DebitAsync(
            envelope.Id, orderId.ResultValue, amount.ResultValue, envelope.EventType == FundsEventTypes.OrderFilled, cancellationToken);
        await CompleteAsync(envelope, message.OrderId, result, cancellationToken);
    }

    private async Task ProcessClosureAsync(EventEnvelope<JsonElement> envelope, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var message = Read<OrderClosureMessage>(envelope, options);
        var orderId = message is null ? default : OrderId.TryCreate(message.OrderId);
        if (message is null || orderId.IsError)
        {
            logger.LogWarning("{EventType} {EnvelopeId} is invalid; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        var result = await store.ReleaseAsync(envelope.Id, orderId.ResultValue, cancellationToken);
        await CompleteAsync(envelope, message.OrderId, result, cancellationToken);
    }

    private Task CompleteAsync(
        EventEnvelope<JsonElement> envelope,
        Guid orderId,
        Result<OrderFundsOutcome, IAccountError> result,
        CancellationToken cancellationToken)
    {
        if (result.IsError)
        {
            // A rule violation on settlement is not retryable data; infrastructure/state errors are the same signal for redelivery.
            throw new InvalidOperationException(
                $"Could not settle {envelope.EventType} {envelope.Id} of order {orderId}: {result.ErrorValue.GetType().Name}.");
        }

        if (result.ResultValue != OrderFundsOutcome.UnknownOrder)
        {
            _fillAttempts.TryRemove(envelope.Id, out _);
            logger.LogInformation("{EventType} of order {OrderId} settled ({Outcome}).", envelope.EventType, orderId, result.ResultValue);
            return Task.CompletedTask;
        }

        // The reservation may still be in flight: redeliver a few times, then give up.
        var attempts = _fillAttempts.AddOrUpdate(envelope.Id, 1, (_, current) => current + 1);
        if (attempts >= MaxFillAttempts)
        {
            _fillAttempts.TryRemove(envelope.Id, out _);
            logger.LogWarning(
                "{EventType} {EnvelopeId}: no reservation for order {OrderId} after {Attempts} attempts; discarded.",
                envelope.EventType, envelope.Id, orderId, attempts);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException($"Reservation of order {orderId} is not available yet (attempt {attempts}).");
    }

    private async Task<Result<decimal, IAccountError>> ComputeAmountAsync(OrderSubmittedMessage message, CancellationToken cancellationToken)
    {
        if (message.Quantity <= 0)
            return Result<decimal, IAccountError>.Error(new InvalidReservationAmount());

        switch (message.Type)
        {
            case OrderType.Limit when message.Price is { } price:
                return OrderFundsCalculator.ForLimit(message.Quantity, price);
            case OrderType.Market when !string.IsNullOrWhiteSpace(message.Ticker):
                var ask = await offerBook.GetBestAskAsync(message.Ticker.Trim().ToUpperInvariant(), cancellationToken);
                return ask.IsError
                    ? Result<decimal, IAccountError>.Error(ask.ErrorValue)
                    : OrderFundsCalculator.ForMarket(message.Quantity, ask.ResultValue);
            default:
                return Result<decimal, IAccountError>.Error(new InvalidReservationAmount());
        }
    }

    private async Task RejectAsync(
        Guid envelopeId,
        OrderId orderId,
        OrderSubmittedMessage message,
        IAccountError error,
        CancellationToken cancellationToken)
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
            throw new InvalidOperationException($"Could not reserve funds of order {message.OrderId}: {error.GetType().Name}.");

        logger.LogWarning("Order {OrderId}: reservation failed ({Kind}).", message.OrderId, kind);
        var failure = new FundsReservationResponse(
            Guid.CreateVersion7(),
            FundsEventTypes.ReservationResult,
            Result<FundsReservedMessage, FundsReservationError>.Error(
                new FundsReservationError(message.OrderId, message.AccountId, kind.Value, error.GetType().Name)));
        await store.RejectAsync(envelopeId, orderId, kind.Value.ToString(), failure, cancellationToken);
    }

    private EventEnvelope<JsonElement>? ReadEnvelope(string body, JsonSerializerOptions options)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(body, options);
            if (envelope is not null && !string.IsNullOrWhiteSpace(envelope.EventType) &&
                envelope.Id != Guid.Empty && envelope.Id.Version == 7)
                return envelope;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Received a malformed message; discarded.");
            return null;
        }

        logger.LogWarning("Received a message without a valid envelope id or event type; discarded.");
        return null;
    }

    private T? Read<T>(EventEnvelope<JsonElement> envelope, JsonSerializerOptions options) where T : class
    {
        try
        {
            return envelope.Event.Deserialize<T>(options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "{EventType} {EnvelopeId} has an invalid payload.", envelope.EventType, envelope.Id);
            return null;
        }
    }
}
