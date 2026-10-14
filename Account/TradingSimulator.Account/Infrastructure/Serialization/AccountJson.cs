using System.Text.Json;
using System.Text.Json.Serialization;
using Moonad;
using TradingSimulator.Account.Infrastructure.Messaging;

namespace TradingSimulator.Account.Infrastructure.Serialization;

public static class AccountJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new FlexibleGuidJsonConverter());
        options.Converters.Add(new ResultJsonConverter<FundsReservedMessage, FundsReservationError>());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
