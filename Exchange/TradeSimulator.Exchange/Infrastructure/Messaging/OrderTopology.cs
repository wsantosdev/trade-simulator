namespace TradeSimulator.Exchange.Infrastructure.Messaging;

public static class OrderTopology
{
    // Orders -> Exchange commands: Orders' EventEnvelope, routing key = event type.
    public const string CommandsExchange = "trade-simulator-order-commands";
    public const string CommandsQueue = "trade-simulator.exchange.order.commands";
    public static readonly string[] CommandRoutingKeys = ["OrderDispatched", "OrderCancelRequested", "OrderReplaceRequested"];

    // Order state events: routing key = event type. Consumers declare and bind their own queues.
    public const string EventsExchange = "trade-simulator.exchange.order.events";
}
