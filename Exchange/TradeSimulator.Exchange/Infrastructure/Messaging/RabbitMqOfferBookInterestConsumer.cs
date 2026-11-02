using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Exchange.Infrastructure.Serialization;
using TradeSimulator.Exchange.Infrastructure.Telemetry;
using TradeSimulator.Exchange.Processors.OfferBook;

namespace TradeSimulator.Exchange.Infrastructure.Messaging;

public sealed class RabbitMqOfferBookInterestConsumer(
    IConfiguration configuration,
    OfferBookInterestProcessor processor,
    ILogger<RabbitMqOfferBookInterestConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    private readonly ILogger _logger = logger;

    protected override ServiceTelemetry Telemetry => ExchangeTelemetry.Service;
    protected override string ExchangeName => OfferBookTopology.InterestExchange;
    protected override string QueueName => OfferBookTopology.InterestQueue;
    protected override IReadOnlyList<string> RoutingKeys => ["#"];
    // Interest is a heartbeat and snapshots are requested again on a gap, so a failure is not retried.
    protected override bool AutoAck => true;
    protected override IDictionary<string, object?> QueueArguments =>
        new Dictionary<string, object?> { ["x-message-ttl"] = OfferBookTopology.InterestMessageTtlMilliseconds };

    protected override async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var command = OfferBookInterestParser.Parse(body, MessageJson.Options);
        if (command.IsError)
            _logger.LogWarning("Dropped a malformed offer book interest message.");
        else
            await processor.ProcessAsync(command.ResultValue, cancellationToken);
    }
}
