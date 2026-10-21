using Moonad;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Features.QueryOrderHistory;

// Outbound port.
public interface IOrderHistory
{
    Task<Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>> GetAsync(AccountId accountId, OrderId orderId, CancellationToken cancellationToken);
}
