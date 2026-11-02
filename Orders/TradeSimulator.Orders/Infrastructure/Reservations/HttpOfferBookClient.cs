using System.Net;
using System.Text.Json;
using TradeSimulator.Orders.Processors;

namespace TradeSimulator.Orders.Infrastructure.Reservations;

public sealed class HttpOfferBookClient(HttpClient http, ILogger<HttpOfferBookClient> logger) : IOfferBookClient
{
    public async Task<decimal?> GetBestAskAsync(string ticker, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"api/offerbook/{Uri.EscapeDataString(ticker)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("asks", out var asks) && asks.ValueKind == JsonValueKind.Array)
            {
                foreach (var ask in asks.EnumerateArray())
                {
                    if (ask.TryGetProperty("price", out var price) && price.TryGetDecimal(out var value) && value > 0)
                        return value;
                    break;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException
                                              or FormatException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Offer book of {Ticker} could not be read.", ticker);
            return null;
        }
    }
}
