using TradingSimulator.Account.Common;
using TradingSimulator.Account.Infrastructure.Serialization;

namespace TradingSimulator.Account.Infrastructure.Messaging;

public sealed class OrderSubmittedConsumer(
    IConfiguration configuration,
    OrderFundsProcessor processor,
    ILogger<OrderSubmittedConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trading-simulator-order-commands";
    public const string Queue = "trading-simulator.account.order.submitted";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => [FundsEventTypes.OrderSubmitted];

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        processor.ProcessOrderSubmittedAsync(body, AccountJson.Options, cancellationToken);
}
