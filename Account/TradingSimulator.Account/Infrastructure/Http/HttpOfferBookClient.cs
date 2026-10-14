using System.Net;
using System.Text.Json;
using Moonad;
using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Infrastructure.Http;

public sealed class HttpOfferBookClient(HttpClient http, ILogger<HttpOfferBookClient> logger) : IOfferBookClient
{
    public async Task<Result<decimal, IAccountError>> GetBestAskAsync(string ticker, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"api/offerbook/{Uri.EscapeDataString(ticker)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Unavailable(ticker, "no offer book");
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

            return Unavailable(ticker, "no asks");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException
                                              or FormatException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Offer book of {Ticker} could not be read.", ticker);
            return new PriceUnavailable();
        }
    }

    private Result<decimal, IAccountError> Unavailable(string ticker, string reason)
    {
        logger.LogWarning("Price of {Ticker} unavailable: {Reason}.", ticker, reason);
        return new PriceUnavailable();
    }
}
