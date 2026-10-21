using TradeSimulator.OfferBook.Processors.OfferBookUpdates;
using Microsoft.AspNetCore.SignalR;
using TradeSimulator.OfferBook.Common;
using TradeSimulator.OfferBook.Domain.ValueObjects;
using TradeSimulator.OfferBook.Infrastructure.Telemetry;

namespace TradeSimulator.OfferBook.Infrastructure.Realtime;

public sealed class SignalROfferBookNotifier(
    IHubContext<OfferBookHub> hub,
    ILogger<SignalROfferBookNotifier> logger) : IOfferBookNotifier
{
    // Real-time delivery is best effort: a lost delta is detected by the client through the sequence and a snapshot recovers it.
    public Task SnapshotAsync(OfferBookSnapshot snapshot, CancellationToken cancellationToken) =>
        SendAsync(snapshot.Ticker, "OfferBookSnapshot", snapshot, cancellationToken);

    public Task DeltaAsync(OfferBookDelta delta, CancellationToken cancellationToken) =>
        SendAsync(delta.Ticker, "OfferBookDelta", delta, cancellationToken);

    private async Task SendAsync(string ticker, string method, object message, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(OfferBookHub.GroupFor(ticker)).SendAsync(method, message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            OfferBookTelemetry.PushFailed(method);
            logger.LogWarning(exception, "Could not push {Method} of {Ticker}.", method, ticker);
        }
    }
}
