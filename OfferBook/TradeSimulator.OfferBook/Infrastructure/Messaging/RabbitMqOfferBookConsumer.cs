using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.OfferBook.Common;
using TradeSimulator.OfferBook.Infrastructure.Serialization;
using TradeSimulator.OfferBook.Infrastructure.Telemetry;
using TradeSimulator.OfferBook.Processors.OfferBookUpdates;

namespace TradeSimulator.OfferBook.Infrastructure.Messaging;

public sealed class RabbitMqOfferBookConsumer(
    IConfiguration configuration,
    OfferBookProcessor processor,
    ILogger<RabbitMqOfferBookConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    private readonly ILogger _logger = logger;

    protected override ServiceTelemetry Telemetry => OfferBookTelemetry.Service;
    protected override string ExchangeName => OfferBookTopology.BookEventsExchange;
    protected override string QueueName => OfferBookTopology.BookQueue;
    protected override IReadOnlyList<string> RoutingKeys => ["offerbook.*"];
    // Offer books are perishable and replaced every second, so no acknowledgement is needed.
    protected override bool AutoAck => true;
    protected override IDictionary<string, object?> QueueArguments => new Dictionary<string, object?>
    {
        ["x-message-ttl"] = OfferBookTopology.BookMessageTtlMilliseconds,
        ["x-max-length"] = OfferBookTopology.BookMaxLength
    };

    protected override async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var result = await processor.ProcessAsync(body, OfferBookJson.Options, cancellationToken);
        if (result.IsError)
            _logger.LogWarning("Dropped a malformed offer book message.");
    }
}
