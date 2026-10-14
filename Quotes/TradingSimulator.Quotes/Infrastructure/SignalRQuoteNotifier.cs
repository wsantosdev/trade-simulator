using Microsoft.AspNetCore.SignalR;
using TradingSimulator.Quotes.Api.Hubs;
using TradingSimulator.Quotes.Application;

namespace TradingSimulator.Quotes.Infrastructure;

public sealed class SignalRQuoteNotifier(IHubContext<QuotesHub> hub, ILogger<SignalRQuoteNotifier> logger) : IQuoteNotifier
{
    // Real-time delivery is best effort: the next tick replaces a lost one.
    public async Task QuoteUpdatedAsync(Quote quote, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(QuotesHub.GroupFor(quote.Ticker)).SendAsync("QuoteUpdated", quote, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not push the quote of {Ticker}.", quote.Ticker);
        }
    }
}
