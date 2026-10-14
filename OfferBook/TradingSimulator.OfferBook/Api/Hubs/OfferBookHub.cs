using Microsoft.AspNetCore.SignalR;
using TradingSimulator.OfferBook.Application;
using TradingSimulator.OfferBook.Domain;

namespace TradingSimulator.OfferBook.Api.Hubs;

public sealed class OfferBookHub(
    OfferBookSubscriptions subscriptions,
    LatestOfferBooks latest,
    OfferBookInterestHeartbeat heartbeat) : Hub
{
    public const string Route = "/hubs/offerbook";

    public static string GroupFor(string ticker) => $"ticker-{ticker}";

    // Returns the last known book so the client does not wait for the next snapshot.
    public async Task<OfferBookSnapshot?> Subscribe(string ticker)
    {
        var parsed = Ticker.TryCreate(ticker);
        if (parsed.IsError)
            throw new HubException("Invalid ticker.");

        var value = parsed.ResultValue;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(value.Value));
        if (subscriptions.Subscribe(Context.ConnectionId, value))
            await heartbeat.FirstSubscriberAsync(value.Value, Context.ConnectionAborted);
        return latest.Find(value);
    }

    public async Task Unsubscribe(string ticker)
    {
        var parsed = Ticker.TryCreate(ticker);
        if (parsed.IsError)
            return;

        subscriptions.Unsubscribe(Context.ConnectionId, parsed.ResultValue);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(parsed.ResultValue.Value));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
