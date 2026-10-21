using Moonad;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.Entities;
using TradeSimulator.Orders.Domain.Enums;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Features.SubmitOrder;

public sealed record SubmitOrderCommand(
    Guid AccountId,
    string? Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    string? RequestHash);

// Inbound port.
public interface ISubmitOrder
{
    Task<Result<Order, IOrderError>> ExecuteAsync(SubmitOrderCommand command, CancellationToken cancellationToken);
}

public sealed class SubmitOrderHandler(IOrderStore store, IOrderNotifier notifier, TimeProvider clock, IOrdersMetrics metrics) : ISubmitOrder
{
    public async Task<Result<Order, IOrderError>> ExecuteAsync(SubmitOrderCommand command, CancellationToken cancellationToken)
    {
        var order = Order.Send(
            OrderId.New(),
            command.AccountId,
            command.Ticker,
            command.Side,
            command.Type,
            command.Quantity,
            command.Price,
            command.RequestHash,
            clock.GetUtcNow());
        if (order.IsError)
        {
            metrics.OperationCompleted("submit", order.ErrorValue);
            return order;
        }

        var stored = await store.SendAsync(order.ResultValue, cancellationToken);
        metrics.OperationCompleted("submit", stored.IsError ? stored.ErrorValue : null);
        if (stored.IsError)
            return stored;

        if (stored.ResultValue.Id == order.ResultValue.Id)
        {
            metrics.OrderSubmitted(command.Side.ToString(), command.Type.ToString());
            await notifier.OrderCreatedAsync(OrderView.From(stored.ResultValue), cancellationToken);
        }
        return stored;
    }
}
