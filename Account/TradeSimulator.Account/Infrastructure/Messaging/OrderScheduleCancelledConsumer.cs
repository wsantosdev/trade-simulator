using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Processors.OrderFunds;

namespace TradeSimulator.Account.Infrastructure.Messaging;

// Funds are now reserved synchronously by Orders; the only command event still consumed is the scheduled-order cancellation.
public sealed class OrderScheduleCancelledConsumer(
    IConfiguration configuration,
    OrderFundsProcessor processor,
    ILogger<OrderScheduleCancelledConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trade-simulator-order-commands";
    public const string Queue = "trade-simulator.account.order.submitted";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => [FundsEventTypes.OrderScheduleCancelled];

    protected override async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = EnvelopeReader.Read(body, logger);
        if (envelope is null || envelope.EventType != FundsEventTypes.OrderScheduleCancelled)
            return;

        var closure = EnvelopeReader.ReadPayload<OrderClosureMessage>(envelope, logger);
        var closedId = closure is null ? default : OrderId.TryCreate(closure.OrderId);
        if (closure is null || closedId.IsError)
        {
            logger.LogWarning("OrderScheduleCancelled {EnvelopeId} has no valid order id; discarded.", envelope.Id);
            return;
        }

        await processor.SettleClosureAsync(envelope.Id, closedId.ResultValue, cancellationToken);
    }
}
