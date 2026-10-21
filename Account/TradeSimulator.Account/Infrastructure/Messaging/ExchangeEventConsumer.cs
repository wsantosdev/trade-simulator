using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Processors.OrderFunds;

namespace TradeSimulator.Account.Infrastructure.Messaging;

public sealed class ExchangeEventConsumer(
    IConfiguration configuration,
    OrderFundsProcessor processor,
    ILogger<ExchangeEventConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trade-simulator.exchange.order.events";
    public const string Queue = "trade-simulator.account.order.events";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;

    protected override IReadOnlyList<string> RoutingKeys =>
    [
        FundsEventTypes.OrderPartiallyFilled,
        FundsEventTypes.OrderFilled,
        FundsEventTypes.OrderCancelled,
        FundsEventTypes.OrderRejected
    ];

    protected override async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = EnvelopeReader.Read(body, logger);
        if (envelope is null)
            return;

        switch (envelope.EventType)
        {
            case FundsEventTypes.OrderPartiallyFilled:
            case FundsEventTypes.OrderFilled:
                await HandleFillAsync(envelope, cancellationToken);
                break;
            case FundsEventTypes.OrderCancelled:
            case FundsEventTypes.OrderRejected:
                await HandleClosureAsync(envelope, cancellationToken);
                break;
            default:
                logger.LogDebug("Discarded exchange event {EventType}.", envelope.EventType);
                break;
        }
    }

    private async Task HandleFillAsync(EventEnvelope<System.Text.Json.JsonElement> envelope, CancellationToken cancellationToken)
    {
        var message = EnvelopeReader.ReadPayload<OrderFillMessage>(envelope, logger);
        var orderId = message is null ? default : OrderId.TryCreate(message.OrderId);
        var price = message?.Price ?? message?.ExecutionPrice;
        if (message is null || orderId.IsError || price is null)
        {
            logger.LogWarning("{EventType} {EnvelopeId} is invalid; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        await processor.SettleFillAsync(
            envelope.Id, orderId.ResultValue, message.ExecutedQuantity, price.Value,
            envelope.EventType == FundsEventTypes.OrderFilled, cancellationToken);
    }

    private async Task HandleClosureAsync(EventEnvelope<System.Text.Json.JsonElement> envelope, CancellationToken cancellationToken)
    {
        var message = EnvelopeReader.ReadPayload<OrderClosureMessage>(envelope, logger);
        var orderId = message is null ? default : OrderId.TryCreate(message.OrderId);
        if (message is null || orderId.IsError)
        {
            logger.LogWarning("{EventType} {EnvelopeId} is invalid; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        await processor.SettleClosureAsync(envelope.Id, orderId.ResultValue, cancellationToken);
    }
}
