using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Quotes.Common;
using TradeSimulator.Quotes.Infrastructure.Serialization;
using TradeSimulator.Quotes.Infrastructure.Telemetry;
using TradeSimulator.Quotes.Processors.QuoteUpdates;

namespace TradeSimulator.Quotes.Infrastructure.Messaging;

public sealed class RabbitMqQuoteConsumer(
    IConfiguration configuration,
    QuoteProcessor processor,
    ILogger<RabbitMqQuoteConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    private readonly ILogger _logger = logger;

    protected override ServiceTelemetry Telemetry => QuotesTelemetry.Service;
    protected override string ExchangeName => QuoteTopology.QuoteEventsExchange;
    protected override string QueueName => QuoteTopology.QuoteQueue;
    protected override IReadOnlyList<string> RoutingKeys => ["quote.*"];
    // Quotes are perishable and replaced every second, so no acknowledgement is needed.
    protected override bool AutoAck => true;
    protected override IDictionary<string, object?> QueueArguments => new Dictionary<string, object?>
    {
        ["x-message-ttl"] = QuoteTopology.QuoteMessageTtlMilliseconds,
        ["x-max-length"] = QuoteTopology.QuoteMaxLength
    };

    protected override async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var result = await processor.ProcessAsync(body, QuoteJson.Options, cancellationToken);
        if (result.IsError)
            _logger.LogWarning("Dropped a malformed quote message.");
    }
}
