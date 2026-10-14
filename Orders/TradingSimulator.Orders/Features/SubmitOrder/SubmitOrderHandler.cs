using Moonad;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.Entities;
using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Features.SubmitOrder;

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

public sealed class SubmitOrderHandler(IOrderStore store, IOrderNotifier notifier, TimeProvider clock) : ISubmitOrder
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
            return order;

        var stored = await store.SendAsync(order.ResultValue, cancellationToken);
        if (stored.IsError)
            return stored;

        if (stored.ResultValue.Id == order.ResultValue.Id)
            await notifier.OrderCreatedAsync(OrderView.From(stored.ResultValue), cancellationToken);
        return stored;
    }
}
