using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Processors.OrderFunds;

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

    protected override async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = EnvelopeReader.Read(body, logger);
        if (envelope is null || envelope.EventType != FundsEventTypes.OrderSubmitted)
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
