using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeSimulator.OfferBook.Infrastructure.Serialization;

public static class OfferBookJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
