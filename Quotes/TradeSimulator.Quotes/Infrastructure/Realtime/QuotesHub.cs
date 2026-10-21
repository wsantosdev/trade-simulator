using TradeSimulator.Quotes.Processors.QuoteInterest;
using Microsoft.AspNetCore.SignalR;
using TradeSimulator.Quotes.Common;
using TradeSimulator.Quotes.Domain.ValueObjects;

namespace TradeSimulator.Quotes.Infrastructure.Realtime;

public sealed class QuotesHub(
    QuoteSubscriptions subscriptions,
    LatestQuotes latest,
    QuoteInterestHeartbeat heartbeat) : Hub
{
    public const string Route = "/hubs/quotes";

    public static string GroupFor(string ticker) => $"ticker-{ticker}";

    // Returns the last known quote so the client does not wait for the next tick.
    public async Task<Quote?> Subscribe(string ticker)
    {
        var parsed = Ticker.TryCreate(ticker);
        if (parsed.IsError)
            throw new HubException("Invalid ticker.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(parsed.ResultValue.Value));
        if (subscriptions.Subscribe(Context.ConnectionId, parsed.ResultValue))
            await heartbeat.PublishAsync(Context.ConnectionAborted);
        return latest.Find(parsed.ResultValue);
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
