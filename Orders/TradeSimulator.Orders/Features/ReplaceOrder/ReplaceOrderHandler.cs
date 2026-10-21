using TradeSimulator.Orders.Features.Shared;
using Moonad;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.Entities;

namespace TradeSimulator.Orders.Features.ReplaceOrder;

// Inbound port.
public interface IReplaceOrder
{
    Task<Result<Order, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, decimal price, CancellationToken cancellationToken);
}

public sealed class ReplaceOrderHandler(OrderModifier modifier, IOrdersMetrics metrics) : IReplaceOrder
{
    public async Task<Result<Order, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, decimal price, CancellationToken cancellationToken)
    {
        var result = await modifier.ModifyAsync(accountId, orderId, (order, now) => order.RequestReplace(price, now), cancellationToken);
        metrics.OperationCompleted("replace", result.IsError ? result.ErrorValue : null);
        return result;
    }
}
