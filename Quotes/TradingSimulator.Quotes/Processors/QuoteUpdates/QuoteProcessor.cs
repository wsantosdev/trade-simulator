using TradingSimulator.Quotes.Common;
using System.Text.Json;
using Moonad;
using TradingSimulator.Quotes.Domain;
using TradingSimulator.Quotes.Domain.ValueObjects;

namespace TradingSimulator.Quotes.Processors.QuoteUpdates;

public sealed class QuoteProcessor(LatestQuotes latest, IQuoteNotifier notifier)
{
    // A malformed message is reported as an error and must be dropped, never retried.
    public async Task<Result<Quote, IQuotesError>> ProcessAsync(
        string body, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        EventEnvelope<Quote>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<Quote>>(body, options);
        }
        catch (JsonException)
        {
            return new MalformedMessage();
        }

        if (envelope?.Event is null || envelope.EventType != nameof(Quote) || Ticker.TryCreate(envelope.Event.Ticker).IsError)
            return new MalformedMessage();

        latest.Update(envelope.Event);
        await notifier.QuoteUpdatedAsync(envelope.Event, cancellationToken);
        return envelope.Event;
    }
}
