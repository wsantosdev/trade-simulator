using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Processors.OrderFunds;

namespace TradeSimulator.Account.Infrastructure.Messaging;

public sealed class OrderSubmittedConsumer(
    IConfiguration configuration,
    OrderFundsProcessor processor,
    ILogger<OrderSubmittedConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trade-simulator-order-commands";
    public const string Queue = "trade-simulator.account.order.submitted";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => [FundsEventTypes.OrderSubmitted, FundsEventTypes.OrderScheduleCancelled];

    protected override async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = EnvelopeReader.Read(body, logger);
        if (envelope is null)
            return;

        if (envelope.EventType == FundsEventTypes.OrderScheduleCancelled)
        {
            // Cancelled in Orders before reaching the Exchange: release the whole reservation.
            var closure = EnvelopeReader.ReadPayload<OrderClosureMessage>(envelope, logger);
            var closedId = closure is null ? default : OrderId.TryCreate(closure.OrderId);
            if (closure is null || closedId.IsError)
            {
                logger.LogWarning("OrderScheduleCancelled {EnvelopeId} has no valid order id; discarded.", envelope.Id);
                return;
            }

            await processor.SettleClosureAsync(envelope.Id, closedId.ResultValue, cancellationToken);
            return;
        }

        if (envelope.EventType != FundsEventTypes.OrderSubmitted)
            return;

        var message = EnvelopeReader.ReadPayload<OrderSubmittedMessage>(envelope, logger);
        var orderId = message is null ? default : OrderId.TryCreate(message.OrderId);
        if (message is null || orderId.IsError)
        {
            logger.LogWarning("OrderSubmitted {EnvelopeId} has no valid order id; discarded.", envelope.Id);
            return;
        }

        var submission = new OrderSubmission(
            orderId.ResultValue, message.AccountId, message.Ticker, message.Side, message.Type, message.Quantity, message.Price);
        await processor.ReserveAsync(envelope.Id, submission, cancellationToken);
    }
}
