using TradingSimulator.Orders.Features.Shared;
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

public sealed class ReplaceOrderHandler(OrderModifier modifier, IOrdersMetrics metrics) : IReplaceOrder
{
    public async Task<Result<Order, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, decimal price, CancellationToken cancellationToken)
    {
        var result = await modifier.ModifyAsync(accountId, orderId, (order, now) => order.RequestReplace(price, now), cancellationToken);
        metrics.OperationCompleted("replace", result.IsError ? result.ErrorValue : null);
        return result;
    }
}
