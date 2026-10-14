using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using TradingSimulator.Portfolio.Domain;

namespace TradingSimulator.Portfolio.Application;

public sealed class PortfolioCommandProcessor(
    IAssetStore store,
    IIdempotencyStore idempotency,
    IResponsePublisher responses,
    IAssetNotifier notifier,
    TimeProvider clock,
    ILogger<PortfolioCommandProcessor> logger)
{
    private const int MaxAttempts = 5;
    public const int MaxSettleAttempts = 5;

    private readonly ConcurrentDictionary<Guid, int> _settleAttempts = new();

    private enum Source { Commands, Orders, Exchange }

    private enum Operation { Deposit, Withdraw, Block, Unblock }

    private sealed record Command(Operation Operation, Guid AccountId, string? Ticker, long Quantity, Guid? OrderId = null);

    private sealed record Context(Guid EnvelopeId, Guid? AccountId, string? Ticker, long? Quantity, Guid? OrderId = null);

    // Throws only on infrastructure failures (the message must be redelivered and the key is released);
    // business and validation errors are answered with a failure response.
    public Task ProcessCommandAsync(string body, JsonSerializerOptions options, CancellationToken cancellationToken) =>
        ProcessAsync(body, options, Source.Commands, cancellationToken);

    // Consumes the Orders events: a sell OrderSubmitted blocks the quantity and is answered with an
    // AssetReservationResult; everything else (buy orders included) is discarded.
    public Task ProcessOrderEventAsync(string body, JsonSerializerOptions options, CancellationToken cancellationToken) =>
        ProcessAsync(body, options, Source.Orders, cancellationToken);

    // Consumes the Exchange events: fills withdraw the executed quantity from the blocked one of the order,
    // the final fill, cancel and reject unblock what is left.
    public Task ProcessExchangeEventAsync(string body, JsonSerializerOptions options, CancellationToken cancellationToken) =>
        ProcessAsync(body, options, Source.Exchange, cancellationToken);

    private async Task ProcessAsync(string body, JsonSerializerOptions options, Source source, CancellationToken cancellationToken)
    {
        EventEnvelope<JsonElement>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(body, options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Received a malformed message.");
            if (source != Source.Commands)
                return;
            await RejectMalformedAsync(new Context(Guid.Empty, null, null, null), new MalformedCommand(), body, cancellationToken);
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.EventType))
        {
            logger.LogWarning("Received a message without an event type.");
            if (source != Source.Commands)
                return;
            await RejectMalformedAsync(new Context(Guid.Empty, null, null, null), new MalformedCommand(), body, cancellationToken);
            return;
        }

        if (source == Source.Orders && envelope.EventType != PortfolioEventTypes.OrderSubmitted ||
            source == Source.Exchange && !IsExchangeEvent(envelope.EventType))
        {
            logger.LogDebug("Discarded order event {EventType}.", envelope.EventType);
            return;
        }

        if (envelope.Id == Guid.Empty || envelope.Id.Version != 7)
        {
            if (source != Source.Commands)
            {
                logger.LogWarning("Discarded {EventType} with an invalid envelope id.", envelope.EventType);
                return;
            }

            await RejectMalformedAsync(new Context(envelope.Id, null, null, null), new InvalidEnvelopeId(), body, cancellationToken);
            return;
        }

        var lease = await idempotency.TryLockAsync(envelope.Id, cancellationToken);
        if (lease is null)
        {
            // Another worker is processing it right now: redeliver later instead of dropping the message.
            throw new InvalidOperationException($"Envelope {envelope.Id} is being processed by another worker.");
        }

        await using (lease)
        {
            if (await lease.WasProcessedAsync(cancellationToken))
            {
                logger.LogInformation("Rejected duplicated envelope {EnvelopeId}.", envelope.Id);
                return;
            }

            // An infrastructure failure propagates before the key is written, so the redelivery is processed again.
            if (source == Source.Exchange)
                await HandleExchangeAsync(envelope, options, cancellationToken);
            else
                await HandleAsync(envelope, options, body, source, cancellationToken);
            await lease.MarkProcessedAsync(cancellationToken);
        }
    }

    private async Task HandleAsync(EventEnvelope<JsonElement> envelope, JsonSerializerOptions options, string body, Source source, CancellationToken cancellationToken)
    {
        Command? command;
        try
        {
            command = Parse(envelope.EventType, envelope.Event, options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Command {EventType} {EnvelopeId} has an invalid payload.", envelope.EventType, envelope.Id);
            if (source != Source.Commands)
                return;
            await RejectMalformedAsync(new Context(envelope.Id, null, null, null), new MalformedCommand(), body, cancellationToken);
            return;
        }

        if (command is null)
        {
            if (envelope.EventType == PortfolioEventTypes.OrderSubmitted)
            {
                logger.LogDebug("Discarded buy order {EnvelopeId}.", envelope.Id);
                return;
            }

            await RejectMalformedAsync(new Context(envelope.Id, null, null, null), new UnsupportedCommand(), body, cancellationToken);
            return;
        }

        await ExecuteAsync(envelope.Id, command, cancellationToken);
    }

    private static Command? Parse(string eventType, JsonElement payload, JsonSerializerOptions options)
    {
        T Read<T>() => payload.Deserialize<T>(options) ?? throw new JsonException("Empty payload.");

        switch (eventType)
        {
            case PortfolioEventTypes.Deposit:
                var deposit = Read<DepositMessage>();
                return new Command(Operation.Deposit, deposit.AccountId, deposit.Ticker, deposit.Quantity);
            case PortfolioEventTypes.Withdraw:
                var withdraw = Read<WithdrawMessage>();
                return new Command(Operation.Withdraw, withdraw.AccountId, withdraw.Ticker, withdraw.Quantity);
            case PortfolioEventTypes.Block:
                var block = Read<BlockMessage>();
                return new Command(Operation.Block, block.AccountId, block.Ticker, block.Quantity);
            case PortfolioEventTypes.Unblock:
                var unblock = Read<UnblockMessage>();
                return new Command(Operation.Unblock, unblock.AccountId, unblock.Ticker, unblock.Quantity);
            case PortfolioEventTypes.OrderSubmitted:
                var submitted = Read<OrderSubmittedMessage>();
                return submitted.Side == OrderSide.Sell
                    ? new Command(Operation.Block, submitted.AccountId, submitted.Ticker, submitted.Quantity, submitted.OrderId)
                    : null;
            default:
                return null;
        }
    }

    private async Task ExecuteAsync(Guid envelopeId, Command command, CancellationToken cancellationToken)
    {
        var context = new Context(envelopeId, command.AccountId, command.Ticker, command.Quantity, command.OrderId);

        var account = AccountId.TryCreate(command.AccountId);
        if (account.IsError)
        {
            await FailAsync(context, account.ErrorValue, null, cancellationToken);
            return;
        }

        var ticker = Ticker.TryCreate(command.Ticker);
        if (ticker.IsError)
        {
            await FailAsync(context, ticker.ErrorValue, null, cancellationToken);
            return;
        }

        var quantity = Quantity.TryCreate(command.Quantity);
        if (quantity.IsError)
        {
            await FailAsync(context, quantity.ErrorValue, null, cancellationToken);
            return;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var now = clock.GetUtcNow();
            var found = await store.FindAsync(account.ResultValue, ticker.ResultValue, cancellationToken);
            var creating = false;
            Result<Asset, IPortfolioError> applied;

            if (found.IsError)
            {
                if (found.ErrorValue is not AssetNotFound || command.Operation != Operation.Deposit)
                {
                    await FailAsync(context, found.ErrorValue, null, cancellationToken);
                    return;
                }

                creating = true;
                applied = Asset.Open(AssetId.New(), account.ResultValue, ticker.ResultValue, quantity.ResultValue, now);
            }
            else
            {
                applied = Apply(found.ResultValue, command.Operation, quantity.ResultValue, now);
            }

            if (applied.IsError)
            {
                await FailAsync(context, applied.ErrorValue, found.IsError ? null : found.ResultValue, cancellationToken);
                return;
            }

            var asset = applied.ResultValue;
            Result<Asset, IPortfolioError> saved;
            if (command.OrderId is { } orderId)
            {
                var reservation = new OrderReservation(orderId, command.AccountId, ticker.ResultValue.Value, command.Quantity, command.Quantity, false);
                saved = await store.ReserveAsync(envelopeId, asset, reservation, BlockedResponse(orderId, command), cancellationToken);
            }
            else
            {
                var response = SuccessResponse(envelopeId, asset, command.Quantity);
                saved = creating
                    ? await store.CreateAsync(asset, response, cancellationToken)
                    : await store.AppendAsync(asset, response, cancellationToken);
            }

            if (!saved.IsError)
            {
                logger.LogInformation("Applied {Operation} of {Quantity} to {Ticker} of account {AccountId}.", command.Operation, command.Quantity, ticker.ResultValue, account.ResultValue);
                await NotifyAsync(asset, cancellationToken);
                return;
            }

            if (saved.ErrorValue is DuplicateEnvelope)
            {
                // The outcome of a previous delivery is already durable (crash before the key was written).
                logger.LogInformation("Envelope {EnvelopeId} was already applied; skipping.", envelopeId);
                return;
            }

            if (saved.ErrorValue is not (ConcurrencyConflict or AssetAlreadyExists))
                throw new InvalidOperationException($"Could not persist {command.Operation} for envelope {envelopeId}: {saved.ErrorValue.GetType().Name}.");
        }

        throw new InvalidOperationException($"Could not persist envelope {envelopeId} after {MaxAttempts} concurrent attempts.");
    }

    private static Result<Asset, IPortfolioError> Apply(Asset asset, Operation operation, Quantity quantity, DateTimeOffset now) =>
        operation switch
        {
            Operation.Deposit => asset.Deposit(quantity, now),
            Operation.Withdraw => asset.Withdraw(quantity, now),
            Operation.Block => asset.Block(quantity, now),
            _ => asset.Unblock(quantity, now)
        };

    private async Task NotifyAsync(Asset asset, CancellationToken cancellationToken)
    {
        try
        {
            await notifier.AssetChangedAsync(asset.AccountId, AssetReadModel.From(asset).ToItem(), cancellationToken);
        }
        catch (Exception exception)
        {
            // The state is already committed: a notification failure must never cause a redelivery.
            logger.LogWarning(exception, "Could not notify the change of asset {AssetId}.", asset.Id);
        }
    }

    private static PortfolioResponse SuccessResponse(Guid envelopeId, Asset asset, long requested) =>
        new(Guid.CreateVersion7(), PortfolioEventTypes.CommandResult,
            Result<PortfolioCommandResult, PortfolioCommandError>.Ok(new PortfolioCommandResult(
                envelopeId, asset.Id.Guid, asset.AccountId.Guid, asset.Ticker.Value, requested,
                asset.Available.Value, asset.Blocked.Value, asset.Total.Value)));

    private static AssetReservationResponse BlockedResponse(Guid orderId, Command command) =>
        new(Guid.CreateVersion7(), AssetReservationEvents.ResultEventType,
            Result<AssetsBlocked, AssetBlockError>.Ok(new AssetsBlocked(orderId, command.AccountId, (command.Ticker ?? string.Empty).Trim().ToUpperInvariant(), command.Quantity)));

    private static bool IsExchangeEvent(string eventType) =>
        eventType is PortfolioEventTypes.OrderPartiallyFilled or PortfolioEventTypes.OrderFilled
            or PortfolioEventTypes.OrderCancelled or PortfolioEventTypes.OrderRejected;

    // Order reservations are answered through the durable outbox; plain commands are answered directly.
    private async Task FailAsync(Context context, IPortfolioError error, Asset? asset, CancellationToken cancellationToken)
    {
        if (context.OrderId is not { } orderId)
        {
            await RejectAsync(context, error, asset, cancellationToken);
            return;
        }

        var kind = error switch
        {
            InsufficientAvailableQuantity => AssetBlockErrorKind.InsufficientQuantity,
            AssetNotFound => AssetBlockErrorKind.AssetNotFound,
            InvalidAccountId or InvalidTicker or InvalidQuantity or QuantityAboveLimit or QuantityOverflow =>
                AssetBlockErrorKind.InvalidOrder,
            _ => (AssetBlockErrorKind?)null
        };

        if (kind is null)
            throw new InvalidOperationException($"Could not block the assets of order {orderId}: {error.GetType().Name}.");

        logger.LogWarning("Order {OrderId}: asset block failed ({Kind}).", orderId, kind);
        var failure = new AssetReservationResponse(
            Guid.CreateVersion7(), AssetReservationEvents.ResultEventType,
            Result<AssetsBlocked, AssetBlockError>.Error(new AssetBlockError(
                orderId, context.AccountId ?? Guid.Empty, (context.Ticker ?? string.Empty).Trim().ToUpperInvariant(), kind.Value, error.GetType().Name)));
        await store.RejectReservationAsync(context.EnvelopeId, failure, cancellationToken);
    }

    private async Task HandleExchangeAsync(EventEnvelope<JsonElement> envelope, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var isFill = envelope.EventType is PortfolioEventTypes.OrderPartiallyFilled or PortfolioEventTypes.OrderFilled;
        Guid orderId;
        long executed = 0;
        try
        {
            if (isFill)
            {
                var fill = envelope.Event.Deserialize<OrderFillMessage>(options);
                orderId = fill?.OrderId ?? Guid.Empty;
                executed = Math.Max(0, fill?.ExecutedQuantity ?? 0);
            }
            else
            {
                orderId = envelope.Event.Deserialize<OrderClosureMessage>(options)?.OrderId ?? Guid.Empty;
            }
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "{EventType} {EnvelopeId} has an invalid payload; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        if (orderId == Guid.Empty)
        {
            logger.LogWarning("{EventType} {EnvelopeId} has no valid order id; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        var final = envelope.EventType != PortfolioEventTypes.OrderPartiallyFilled;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var reservation = await store.FindReservationAsync(orderId, cancellationToken);
            if (reservation is null)
            {
                RegisterUnknownOrder(envelope, orderId);
                return;
            }

            _settleAttempts.TryRemove(envelope.Id, out _);
            if (reservation.Closed)
            {
                logger.LogInformation("{EventType} {EnvelopeId}: reservation of order {OrderId} is already closed.", envelope.EventType, envelope.Id, orderId);
                return;
            }

            var account = AccountId.TryCreate(reservation.AccountId);
            var ticker = Ticker.TryCreate(reservation.Ticker);
            if (account.IsError || ticker.IsError)
                throw new InvalidOperationException($"Reservation of order {orderId} is corrupt.");

            var found = await store.FindAsync(account.ResultValue, ticker.ResultValue, cancellationToken);
            if (found.IsError && found.ErrorValue is not AssetNotFound)
                throw new InvalidOperationException($"Could not load the asset of order {orderId}: {found.ErrorValue.GetType().Name}.");

            var asset = found.IsError ? null : found.ResultValue;
            var execute = Math.Min(executed, reservation.Remaining);
            var left = reservation.Remaining - execute;
            var close = final || left == 0;
            var release = close ? left : 0;

            if (asset is not null)
            {
                var now = clock.GetUtcNow();
                // The user may have unblocked by hand; never take more than what is still blocked.
                var withdraw = Math.Min(execute, asset.Blocked.Value);
                var unblock = Math.Min(release, asset.Blocked.Value - withdraw);
                if (withdraw < execute || unblock < release)
                    logger.LogWarning("Order {OrderId}: blocked quantity of the asset is lower than the reservation.", orderId);

                if (withdraw > 0 && asset.Withdraw(Quantity.TryCreate(withdraw).ResultValue, now) is { IsError: true } withdrawn)
                    throw new InvalidOperationException($"Could not withdraw for order {orderId}: {withdrawn.ErrorValue.GetType().Name}.");
                if (unblock > 0 && asset.Unblock(Quantity.TryCreate(unblock).ResultValue, now) is { IsError: true } unblocked)
                    throw new InvalidOperationException($"Could not unblock for order {orderId}: {unblocked.ErrorValue.GetType().Name}.");
            }
            else
            {
                logger.LogWarning("Order {OrderId}: its asset no longer exists; closing the reservation only.", orderId);
            }

            var changed = asset is { UncommittedEvents.Count: > 0 };
            var settled = await store.SettleAsync(
                envelope.Id, asset, orderId, reservation.Remaining, close ? 0 : left, close, cancellationToken);
            if (!settled.IsError)
            {
                logger.LogInformation("{EventType} of order {OrderId} settled.", envelope.EventType, orderId);
                if (changed)
                    await NotifyAsync(asset!, cancellationToken);
                return;
            }

            if (settled.ErrorValue is DuplicateEnvelope)
            {
                logger.LogInformation("Envelope {EnvelopeId} was already applied; skipping.", envelope.Id);
                return;
            }

            if (settled.ErrorValue is not ConcurrencyConflict)
                throw new InvalidOperationException($"Could not settle {envelope.EventType} {envelope.Id}: {settled.ErrorValue.GetType().Name}.");
        }

        throw new InvalidOperationException($"Could not settle envelope {envelope.Id} after {MaxAttempts} concurrent attempts.");
    }

    // The reservation may still be in flight: redeliver a few times, then give up.
    private void RegisterUnknownOrder(EventEnvelope<JsonElement> envelope, Guid orderId)
    {
        var attempts = _settleAttempts.AddOrUpdate(envelope.Id, 1, (_, current) => current + 1);
        if (attempts >= MaxSettleAttempts)
        {
            _settleAttempts.TryRemove(envelope.Id, out _);
            logger.LogWarning(
                "{EventType} {EnvelopeId}: no reservation for order {OrderId} after {Attempts} attempts; discarded.",
                envelope.EventType, envelope.Id, orderId, attempts);
            return;
        }

        throw new InvalidOperationException($"Reservation of order {orderId} is not available yet (attempt {attempts}).");
    }

    private Task RejectMalformedAsync(Context context, IPortfolioError error, string rawMessage, CancellationToken cancellationToken) =>
        RejectAsync(context, error, null, rawMessage, cancellationToken);

    private Task RejectAsync(Context context, IPortfolioError error, Asset? asset, CancellationToken cancellationToken) =>
        RejectAsync(context, error, asset, null, cancellationToken);

    private async Task RejectAsync(Context context, IPortfolioError error, Asset? asset, string? rawMessage, CancellationToken cancellationToken)
    {
        logger.LogWarning("Command {EnvelopeId} failed: {Error}.", context.EnvelopeId, error.GetType().Name);

        var failure = new PortfolioCommandError(
            context.EnvelopeId, asset?.Id.Guid, context.AccountId, context.Ticker, context.Quantity,
            asset?.Available.Value, asset?.Blocked.Value, asset?.Total.Value,
            error.GetType().Name, rawMessage);

        await responses.PublishFailureAsync(
            new PortfolioResponse(Guid.CreateVersion7(), PortfolioEventTypes.CommandResult,
                Result<PortfolioCommandResult, PortfolioCommandError>.Error(failure)),
            cancellationToken);
    }
}
