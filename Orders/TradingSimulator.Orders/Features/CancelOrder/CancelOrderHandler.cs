using Moonad;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.Entities;

namespace TradingSimulator.Orders.Features.CancelOrder;

// Inbound port.
public interface ICancelOrder
{
    Task<Result<Order, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken);
}

public sealed class CancelOrderHandler(OwnedOrderUpdater updater) : ICancelOrder
{
    public Task<Result<Order, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken) =>
        updater.UpdateAsync(accountId, orderId, (order, now) => order.RequestCancel(now), cancellationToken);
}
