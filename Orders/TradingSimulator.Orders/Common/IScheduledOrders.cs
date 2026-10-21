using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Common;

public interface IScheduledOrders
{
    // Oldest first.
    Task<IReadOnlyList<OrderId>> ListAsync(int limit, CancellationToken cancellationToken);
}
