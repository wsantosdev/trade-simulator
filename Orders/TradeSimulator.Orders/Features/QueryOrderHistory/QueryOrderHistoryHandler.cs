using Moonad;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Features.QueryOrderHistory;

public sealed record OrderHistoryEntry(int Sequence, string EventType, DateTimeOffset OccurredAt, string Data);

// Inbound port.
public interface IQueryOrderHistory
{
    Task<Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>> ExecuteAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken);
}

public sealed class QueryOrderHistoryHandler(IOrderHistory history) : IQueryOrderHistory
{
    public async Task<Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>> ExecuteAsync(
        Guid accountId, Guid orderId, CancellationToken cancellationToken)
    {
        var account = AccountId.TryCreate(accountId);
        if (account.IsError)
            return Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>.Error(account.ErrorValue);
        var id = OrderId.TryCreate(orderId);
        if (id.IsError)
            return Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>.Error(id.ErrorValue);

        return await history.GetAsync(account.ResultValue, id.ResultValue, cancellationToken);
    }
}
