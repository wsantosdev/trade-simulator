using TradeSimulator.Orders.Features.Shared;
using Moonad;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.Entities;

namespace TradeSimulator.Orders.Features.CancelOrder;

// Inbound port.
public interface ICancelOrder
{
    Task<Result<Order, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken);
}

public sealed class CancelOrderHandler(OrderModifier modifier, IOrdersMetrics metrics) : ICancelOrder
{
    public async Task<Result<Order, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken)
    {
        var result = await modifier.ModifyAsync(accountId, orderId, (order, now) => order.RequestCancel(now), cancellationToken);
        metrics.OperationCompleted("cancel", result.IsError ? result.ErrorValue : null);
        return result;
    }
}
