using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Exchange.Infrastructure.Serialization;
using TradeSimulator.Exchange.Infrastructure.Telemetry;
using TradeSimulator.Exchange.Processors.OrderCommands;

namespace TradeSimulator.Exchange.Infrastructure.Messaging;

public sealed class RabbitMqOrderCommandConsumer(
    IConfiguration configuration,
    OrderBookEngine engine,
    ILogger<RabbitMqOrderCommandConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    private readonly ILogger _logger = logger;

    protected override ServiceTelemetry Telemetry => ExchangeTelemetry.Service;
    protected override string ExchangeName => OrderTopology.CommandsExchange;
    protected override string QueueName => OrderTopology.CommandsQueue;
    protected override IReadOnlyList<string> RoutingKeys => OrderTopology.CommandRoutingKeys;

    protected override async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var command = OrderCommandParser.Parse(body, MessageJson.Options);
        if (command.IsError)
            _logger.LogWarning("Dropped a malformed order command.");
        else
            await engine.HandleAsync(command.ResultValue, cancellationToken);
    }
}
