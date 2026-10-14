using System.Text.Json;
using Moonad;
using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

public sealed class OfferBookInterestProcessor(OfferBookInterestRegistry registry, OrderBookEngine engine)
{
    // Malformed messages are reported as errors and must be dropped, not retried.
    public async Task<Result<string, IExchangeError>> ProcessAsync(
        string body, JsonSerializerOptions options, CancellationToken cancellationToken)
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
                    registry.Renew(renewed.Tickers.Select(value => Ticker.TryCreate(value))
                        .Where(ticker => !ticker.IsError).Select(ticker => ticker.ResultValue));
                    return nameof(OfferBookInterestRenewed);
                case nameof(OfferBookSnapshotRequested):
                    var requested = payload.Deserialize<OfferBookSnapshotRequested>(options);
                    var requestedTicker = Ticker.TryCreate(requested?.Ticker);
                    if (requestedTicker.IsError)
                        return new MalformedMessage();
                    await engine.PublishSnapshotAsync(requestedTicker.ResultValue, cancellationToken);
                    return nameof(OfferBookSnapshotRequested);
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
