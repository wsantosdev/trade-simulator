using Moonad;
using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Features.QueryOrderHistory;

// Outbound port.
public interface IOrderHistory
{
    Task<Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>> GetAsync(AccountId accountId, OrderId orderId, CancellationToken cancellationToken);
}
