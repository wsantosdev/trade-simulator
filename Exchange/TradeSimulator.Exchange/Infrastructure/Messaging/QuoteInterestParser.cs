using System.Text.Json;
using Moonad;
using TradeSimulator.Exchange.Domain;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Infrastructure.Messaging;

public static class QuoteInterestParser
{
    // Malformed messages are reported as errors and must be dropped, not retried; invalid tickers are skipped.
    public static Result<IReadOnlyList<Ticker>, IExchangeError> Parse(string body, JsonSerializerOptions options)
    {
        EventEnvelope<QuoteInterestRenewed>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<QuoteInterestRenewed>>(body, options);
        }
        catch (JsonException)
        {
            return new MalformedMessage();
        }

        if (envelope?.Event?.Tickers is null || envelope.EventType != nameof(QuoteInterestRenewed))
            return new MalformedMessage();

        return envelope.Event.Tickers.Select(value => Ticker.TryCreate(value))
            .Where(ticker => !ticker.IsError).Select(ticker => ticker.ResultValue).ToList();
    }
}
