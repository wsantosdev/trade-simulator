using System.Text.Json;
using Moonad;
using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

public sealed class QuoteInterestProcessor(QuoteInterestRegistry registry)
{
    // Returns the number of tickers renewed; malformed messages are reported as errors and must be dropped, not retried.
    public Result<int, IExchangeError> Process(string body, JsonSerializerOptions options)
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

        var tickers = new List<Ticker>();
        foreach (var value in envelope.Event.Tickers)
        {
            var ticker = Ticker.TryCreate(value);
            if (!ticker.IsError)
                tickers.Add(ticker.ResultValue);
        }

        registry.Renew(tickers);
        return tickers.Count;
    }
}
