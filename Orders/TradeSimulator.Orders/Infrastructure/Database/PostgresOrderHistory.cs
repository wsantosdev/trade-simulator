using Moonad;
using Npgsql;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.ValueObjects;
using TradeSimulator.Orders.Features.QueryOrderHistory;

namespace TradeSimulator.Orders.Infrastructure.Database;

public sealed class PostgresOrderHistory(NpgsqlDataSource dataSource) : IOrderHistory
{
    public async Task<Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>> GetAsync(
        AccountId accountId, OrderId orderId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT e.sequence, e.event_type, e.occurred_at, e.payload::text
            FROM order_events e
            JOIN order_read_model r ON r.order_id = e.order_id
            WHERE e.order_id = @orderId AND r.account_id = @accountId
            ORDER BY e.sequence;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("orderId", orderId.Guid);
        command.Parameters.AddWithValue("accountId", accountId.Guid);
        var entries = new List<OrderHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(new OrderHistoryEntry(
                reader.GetInt32(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3)));

        if (entries.Count == 0)
            return Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>.Error(new OrderNotFound());
        return Result<IReadOnlyList<OrderHistoryEntry>, IOrderError>.Ok(entries);
    }
}
