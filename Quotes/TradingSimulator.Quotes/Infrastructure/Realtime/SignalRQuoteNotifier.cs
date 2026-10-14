using TradingSimulator.Quotes.Processors.QuoteUpdates;
using Microsoft.AspNetCore.SignalR;
using TradingSimulator.Quotes.Common;
using TradingSimulator.Quotes.Domain.ValueObjects;

namespace TradingSimulator.Quotes.Infrastructure.Realtime;

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
