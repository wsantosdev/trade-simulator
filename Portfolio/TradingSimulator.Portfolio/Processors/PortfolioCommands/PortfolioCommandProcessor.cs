using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Moonad;
using TradingSimulator.Portfolio.Domain;
using TradingSimulator.Portfolio.Domain.Entities;
using TradingSimulator.Portfolio.Common;
using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Processors.PortfolioCommands;

public sealed class PortfolioCommandProcessor(
    IAssetStore store,
    IIdempotencyStore idempotency,
    IResponsePublisher responses,
    IAssetNotifier notifier,
    IPortfolioMetrics metrics,
    TimeProvider clock,
    ILogger<PortfolioCommandProcessor> logger)
{
    private const int MaxAttempts = 5;
    public const int MaxSettleAttempts = 5;

    private readonly ConcurrentDictionary<Guid, int> _settleAttempts = new();

    private sealed record Command(AssetOperation Operation, Guid AccountId, string? Ticker, long Quantity, Guid? OrderId = null);

    private sealed record Context(Guid EnvelopeId, Guid? AccountId, string? Ticker, long? Quantity, Guid? OrderId = null);

    // Throws only on infrastructure failures (the message must be redelivered and the key is released);
    // business errors are answered with a failure response.
    public Task ExecuteAsync(Guid envelopeId, AssetCommand command, CancellationToken cancellationToken) =>
        WithLeaseAsync(envelopeId, token =>
            ApplyAsync(envelopeId, new Command(command.Operation, command.AccountId, command.Ticker, command.Quantity), token),
            cancellationToken);

    // A sell order blocks the quantity and is answered through the reservation outbox.
    public Task ReserveAsync(Guid envelopeId, SellOrder order, CancellationToken cancellationToken) =>
        WithLeaseAsync(envelopeId, token =>
            ApplyAsync(envelopeId, new Command(AssetOperation.Block, order.AccountId, order.Ticker, order.Quantity, order.OrderId), token),
            cancellationToken);

    // Fills withdraw the executed quantity from the blocked one of the order; the final event
    // (last fill, cancel, reject) unblocks what is left.
    public Task SettleOrderAsync(Guid envelopeId, Guid orderId, long executedQuantity, bool final, CancellationToken cancellationToken) =>
        WithLeaseAsync(envelopeId, token => SettleAsync(envelopeId, orderId, executedQuantity, final, token), cancellationToken);

    public async Task RejectMalformedAsync(Guid envelopeId, IPortfolioError error, string rawMessage, CancellationToken cancellationToken)
    {
        var context = new Context(envelopeId, null, null, null);
        if (envelopeId == Guid.Empty || envelopeId.Version != 7)
        {
            await RejectAsync(context, error, null, rawMessage, cancellationToken);
            return;
        }

        await WithLeaseAsync(
            envelopeId, token => RejectAsync(context, error, null, rawMessage, token), cancellationToken);
    }

    private async Task WithLeaseAsync(Guid envelopeId, Func<CancellationToken, Task> handle, CancellationToken cancellationToken)
    {
        var lease = await idempotency.TryLockAsync(envelopeId, cancellationToken);
        if (lease is null)
        {
            // Another worker is processing it right now: redeliver later instead of dropping the message.
            throw new InvalidOperationException($"Envelope {envelopeId} is being processed by another worker.");
        }

        await using (lease)
        {
            if (await lease.WasProcessedAsync(cancellationToken))
            {
                metrics.DuplicateEnvelope();
                logger.LogInformation("Rejected duplicated envelope {EnvelopeId}.", envelopeId);
                return;
            }

            // An infrastructure failure propagates before the key is written, so the redelivery is processed again.
            await handle(cancellationToken);
            await lease.MarkProcessedAsync(cancellationToken);
        }
    }

    private async Task ApplyAsync(Guid envelopeId, Command command, CancellationToken cancellationToken)
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
                if (found.ErrorValue is not AssetNotFound || command.Operation != AssetOperation.Deposit)
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
                var blocked = new AssetsBlocked(orderId, command.AccountId, ticker.ResultValue.Value, command.Quantity);
                saved = await store.ReserveAsync(envelopeId, asset, reservation, blocked, cancellationToken);
            }
            else
            {
                var result = SuccessResult(envelopeId, asset, command.Quantity);
                saved = creating
                    ? await store.CreateAsync(asset, result, cancellationToken)
                    : await store.AppendAsync(asset, result, cancellationToken);
            }

            if (!saved.IsError)
            {
                logger.LogInformation("Applied {Operation} of {Quantity} to {Ticker} of account {AccountId}.", command.Operation, command.Quantity, ticker.ResultValue, account.ResultValue);
                if (command.OrderId is null)
                    metrics.CommandApplied(command.Operation.ToString());
                else
                    metrics.ReservationBlocked();
                await NotifyAsync(asset, cancellationToken);
                return;
            }

            if (saved.ErrorValue is DuplicateEnvelope)
            {
                metrics.DuplicateEnvelope();
                // The outcome of a previous delivery is already durable (crash before the key was written).
                logger.LogInformation("Envelope {EnvelopeId} was already applied; skipping.", envelopeId);
                return;
            }

            if (saved.ErrorValue is not (ConcurrencyConflict or AssetAlreadyExists))
                throw new InvalidOperationException($"Could not persist {command.Operation} for envelope {envelopeId}: {saved.ErrorValue.GetType().Name}.");
        }

        throw new InvalidOperationException($"Could not persist envelope {envelopeId} after {MaxAttempts} concurrent attempts.");
    }

    private static Result<Asset, IPortfolioError> Apply(Asset asset, AssetOperation operation, Quantity quantity, DateTimeOffset now) =>
        operation switch
        {
            AssetOperation.Deposit => asset.Deposit(quantity, now),
            AssetOperation.Withdraw => asset.Withdraw(quantity, now),
            AssetOperation.Block => asset.Block(quantity, now),
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

    private static PortfolioCommandResult SuccessResult(Guid envelopeId, Asset asset, long requested) =>
        new(envelopeId, asset.Id.Guid, asset.AccountId.Guid, asset.Ticker.Value, requested,
            asset.Available.Value, asset.Blocked.Value, asset.Total.Value);

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

        metrics.ReservationRejected(kind.Value.ToString());
        logger.LogWarning("Order {OrderId}: asset block failed ({Kind}).", orderId, kind);
        var failure = new AssetBlockError(
            orderId, context.AccountId ?? Guid.Empty, (context.Ticker ?? string.Empty).Trim().ToUpperInvariant(), kind.Value, error.GetType().Name);
        await store.RejectReservationAsync(context.EnvelopeId, failure, cancellationToken);
    }

    private async Task SettleAsync(Guid envelopeId, Guid orderId, long executed, bool final, CancellationToken cancellationToken)
    {
        var eventName = final ? "closure" : "fill";
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var reservation = await store.FindReservationAsync(orderId, cancellationToken);
            if (reservation is null)
            {
                RegisterUnknownOrder(envelopeId, eventName, orderId);
                return;
            }

            _settleAttempts.TryRemove(envelopeId, out _);
            if (reservation.Closed)
            {
                logger.LogInformation("{EventType} {EnvelopeId}: reservation of order {OrderId} is already closed.", eventName, envelopeId, orderId);
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
                envelopeId, asset, orderId, reservation.Remaining, close ? 0 : left, close, cancellationToken);
            if (!settled.IsError)
            {
                metrics.OrderSettled(eventName);
                logger.LogInformation("{EventType} of order {OrderId} settled.", eventName, orderId);
                if (changed)
                    await NotifyAsync(asset!, cancellationToken);
                return;
            }

            if (settled.ErrorValue is DuplicateEnvelope)
            {
                metrics.DuplicateEnvelope();
                logger.LogInformation("Envelope {EnvelopeId} was already applied; skipping.", envelopeId);
                return;
            }

            if (settled.ErrorValue is not ConcurrencyConflict)
                throw new InvalidOperationException($"Could not settle {eventName} {envelopeId}: {settled.ErrorValue.GetType().Name}.");
        }

        throw new InvalidOperationException($"Could not settle envelope {envelopeId} after {MaxAttempts} concurrent attempts.");
    }

    // The reservation may still be in flight: redeliver a few times, then give up.
    private void RegisterUnknownOrder(Guid envelopeId, string eventName, Guid orderId)
    {
        var attempts = _settleAttempts.AddOrUpdate(envelopeId, 1, (_, current) => current + 1);
        if (attempts >= MaxSettleAttempts)
        {
            _settleAttempts.TryRemove(envelopeId, out _);
            metrics.SettlementDiscarded();
            logger.LogWarning(
                "{EventType} {EnvelopeId}: no reservation for order {OrderId} after {Attempts} attempts; discarded.",
                eventName, envelopeId, orderId, attempts);
            return;
        }

        throw new InvalidOperationException($"Reservation of order {orderId} is not available yet (attempt {attempts}).");
    }

    private Task RejectAsync(Context context, IPortfolioError error, Asset? asset, CancellationToken cancellationToken) =>
        RejectAsync(context, error, asset, null, cancellationToken);

    private async Task RejectAsync(Context context, IPortfolioError error, Asset? asset, string? rawMessage, CancellationToken cancellationToken)
    {
        metrics.CommandRejected(error.GetType().Name);
        logger.LogWarning("Command {EnvelopeId} failed: {Error}.", context.EnvelopeId, error.GetType().Name);

        var failure = new PortfolioCommandError(
            context.EnvelopeId, asset?.Id.Guid, context.AccountId, context.Ticker, context.Quantity,
            asset?.Available.Value, asset?.Blocked.Value, asset?.Total.Value,
            error.GetType().Name, rawMessage);

        await responses.PublishFailureAsync(failure, cancellationToken);
    }
}
