using TradingSimulator.Account.Application;

namespace TradingSimulator.Account.Infrastructure;

public sealed class ExchangeEventConsumer(
    IConfiguration configuration,
    OrderFundsProcessor processor,
    ILogger<ExchangeEventConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trading-simulator.exchange.order.events";
    public const string Queue = "trading-simulator.account.order.events";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;

    protected override IReadOnlyList<string> RoutingKeys =>
    [
        FundsEventTypes.OrderPartiallyFilled,
        FundsEventTypes.OrderFilled,
        FundsEventTypes.OrderCancelled,
        FundsEventTypes.OrderRejected
    ];

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        processor.ProcessExchangeEventAsync(body, AccountJson.Options, cancellationToken);
}
