using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Common;

public interface IScheduledOrders
{
    // Oldest first.
    Task<IReadOnlyList<OrderId>> ListAsync(int limit, CancellationToken cancellationToken);
}
