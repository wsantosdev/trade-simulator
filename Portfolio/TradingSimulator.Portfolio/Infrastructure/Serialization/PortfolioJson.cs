using System.Text.Json;
using System.Text.Json.Serialization;
using TradingSimulator.Portfolio.Common;
using TradingSimulator.Portfolio.Infrastructure.Messaging;

namespace TradingSimulator.Portfolio.Infrastructure.Serialization;

public static class PortfolioJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new FlexibleGuidJsonConverter());
        options.Converters.Add(new AccountIdJsonConverter());
        options.Converters.Add(new AssetIdJsonConverter());
        options.Converters.Add(new TickerJsonConverter());
        options.Converters.Add(new QuantityJsonConverter());
        options.Converters.Add(new ResultJsonConverter<PortfolioCommandResult, PortfolioCommandError>());
        options.Converters.Add(new ResultJsonConverter<AssetsBlocked, AssetBlockError>());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
