using System.Text.Json;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain.ValueObjects;
using TradeSimulator.Orders.Infrastructure.ReadModel;
using TradeSimulator.Orders.Infrastructure.Serialization;

namespace TradeSimulator.Orders.Infrastructure.Messaging;

// Reacts to every order event published by the outbox and refreshes the MongoDB read model.
// The event only says which order changed: the view is rebuilt from the order's stream and written
// only if newer than the stored one, so duplicates and out-of-order deliveries are harmless.
public sealed class OrderProjectionHandler(IOrderStore store, MongoOrderReadModel readModel, ILogger<OrderProjectionHandler> logger)
{
    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        Guid orderId;
        try
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope<OrderEventRef>>(body, OrderJson.Options);
            if (envelope?.Event?.OrderId is not { } id)
            {
                logger.LogWarning("Discarded an order event without an order id.");
                return;
            }

            orderId = id.Guid;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Discarded a malformed order event.");
            return;
        }

        var order = await store.GetAsync(OrderId.TryCreate(orderId).ResultValue, cancellationToken);
        if (order.IsError)
        {
            logger.LogWarning("Order {OrderId} could not be loaded for projection: {Error}.", orderId, order.ErrorValue);
            return;
        }

        if (order.ResultValue.PassedValidation)
            await readModel.UpsertAsync(OrderView.From(order.ResultValue), order.ResultValue.Version, cancellationToken);
        else
            await readModel.RemoveAsync(orderId, cancellationToken);
    }
}

internal sealed record OrderEventRef(OrderId? OrderId);
