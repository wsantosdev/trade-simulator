using Moonad;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.ValueObjects;
using TradeSimulator.Orders.Features.QueryOrderHistory;

namespace TradeSimulator.Orders.Infrastructure.Database;

// The history is the event log itself, read through the event store.
public sealed class OrderEventHistory(IOrderStore store) : IOrderHistory
{
    public async Task<Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>> GetAsync(
        AccountId accountId, OrderId orderId, CancellationToken cancellationToken)
    {
        var order = await store.GetAsync(orderId, cancellationToken);
        if (order.IsError || order.ResultValue.AccountId != accountId || !order.ResultValue.PassedValidation)
            return Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>.Error(new OrderNotFound());

        var events = await store.ReadEventsAsync(orderId, cancellationToken);
        return Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>.Ok(
            events.Select(e => new OrderHistoryEntry(e.Sequence, e.EventType, e.OccurredAt, e.Payload)).ToList());
    }
}
