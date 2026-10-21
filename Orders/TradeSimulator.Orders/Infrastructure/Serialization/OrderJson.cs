using System.Text.Json;
using System.Text.Json.Serialization;
using TradeSimulator.Orders.Infrastructure.Messaging;

namespace TradeSimulator.Orders.Infrastructure.Serialization;

public static class OrderJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new OrderIdJsonConverter());
        options.Converters.Add(new AccountIdJsonConverter());
        options.Converters.Add(new TickerJsonConverter());
        options.Converters.Add(new RequestHashJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ResultJsonConverter<FundsReserved, FundsReservationError>());
        options.Converters.Add(new ResultJsonConverter<AssetsBlocked, AssetBlockError>());
        return options;
    }
}
