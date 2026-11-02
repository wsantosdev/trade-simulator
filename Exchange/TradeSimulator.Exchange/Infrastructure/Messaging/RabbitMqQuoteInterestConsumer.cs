using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Exchange.Infrastructure.Serialization;
using TradeSimulator.Exchange.Infrastructure.Telemetry;
using TradeSimulator.Exchange.Processors.Quotes;

namespace TradeSimulator.Exchange.Infrastructure.Messaging;

public sealed class RabbitMqQuoteInterestConsumer(
    IConfiguration configuration,
    QuoteInterestRegistry registry,
    ILogger<RabbitMqQuoteInterestConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    private readonly ILogger _logger = logger;

    protected override ServiceTelemetry Telemetry => ExchangeTelemetry.Service;
    protected override string ExchangeName => QuoteTopology.InterestExchange;
    protected override string QueueName => QuoteTopology.InterestQueue;
    protected override IReadOnlyList<string> RoutingKeys => ["#"];
    // Interest is a heartbeat: a lost message is replaced by the next one.
    protected override bool AutoAck => true;
    protected override IDictionary<string, object?> QueueArguments =>
        new Dictionary<string, object?> { ["x-message-ttl"] = QuoteTopology.InterestMessageTtlMilliseconds };

    protected override Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var tickers = QuoteInterestParser.Parse(body, MessageJson.Options);
        if (tickers.IsError)
            _logger.LogWarning("Dropped a malformed quote interest message.");
        else
            registry.Renew(tickers.ResultValue);
        return Task.CompletedTask;
    }
}
