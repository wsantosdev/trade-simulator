using System.Text.Json;
using Moonad;
using TradingSimulator.Exchange.Domain;
using TradingSimulator.Exchange.Domain.ValueObjects;
using TradingSimulator.Exchange.Processors.OfferBook;

namespace TradingSimulator.Exchange.Infrastructure.Messaging;

public static class OfferBookInterestParser
{
    // Malformed messages are reported as errors and must be dropped, not retried; invalid tickers are skipped.
    public static Result<OfferBookInterestCommand, IExchangeError> Parse(string body, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("eventType", out var eventType)
                || !root.TryGetProperty("event", out var payload))
                return new MalformedMessage();

            switch (eventType.GetString())
            {
                case nameof(OfferBookInterestRenewed):
                    var renewed = payload.Deserialize<OfferBookInterestRenewed>(options);
                    if (renewed?.Tickers is null)
                        return new MalformedMessage();
                    return new RenewOfferBookInterest(renewed.Tickers.Select(value => Ticker.TryCreate(value))
                        .Where(ticker => !ticker.IsError).Select(ticker => ticker.ResultValue).ToList());
                case nameof(OfferBookSnapshotRequested):
                    var requested = payload.Deserialize<OfferBookSnapshotRequested>(options);
                    var ticker = Ticker.TryCreate(requested?.Ticker);
                    if (ticker.IsError)
                        return new MalformedMessage();
                    return new RequestOfferBookSnapshot(ticker.ResultValue);
                default:
                    return new MalformedMessage();
            }
        }
        catch (JsonException)
        {
            return new MalformedMessage();
        }
    }
}
