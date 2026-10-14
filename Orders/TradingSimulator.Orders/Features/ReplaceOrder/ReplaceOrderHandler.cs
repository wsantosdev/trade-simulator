using Moonad;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.Entities;

namespace TradingSimulator.Orders.Features.ReplaceOrder;

// Inbound port.
public interface IReplaceOrder
{
    Task<Result<Order, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, decimal price, CancellationToken cancellationToken);
}

public sealed class ReplaceOrderHandler(OwnedOrderUpdater updater) : IReplaceOrder
{
    public Task<Result<Order, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, decimal price, CancellationToken cancellationToken) =>
        updater.UpdateAsync(accountId, orderId, (order, now) => order.RequestReplace(price, now), cancellationToken);
}
