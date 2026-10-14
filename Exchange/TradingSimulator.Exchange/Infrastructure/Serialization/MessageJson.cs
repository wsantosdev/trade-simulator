using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingSimulator.Exchange.Infrastructure.Serialization;

public static class MessageJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new OrderIdJsonConverter());
        return options;
    }
}
