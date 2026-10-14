using Npgsql;
using NpgsqlTypes;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Features.QueryOrders;

namespace TradingSimulator.Orders.Infrastructure.Database;

public sealed class PostgresOrderSearch(NpgsqlDataSource dataSource) : IOrderSearch
{
    private const string Columns = """
        order_id, account_id, ticker, side, type, quantity, price, state, executed_quantity,
        remaining_quantity, average_price, last_price, last_trade_at, sent_at, last_action_at
        """;

    public async Task<OrderPage> SearchAsync(OrderSearch search, CancellationToken cancellationToken)
    {
        var column = OrderSortFields.Columns.GetValueOrDefault(search.SortBy, "sent_at");
        var direction = search.Descending ? "DESC" : "ASC";
        var page = Math.Max(search.Page, 1);
        var where = """
            account_id = @accountId
            AND (@ticker::text IS NULL OR ticker = @ticker)
            AND (@state::text IS NULL OR state = @state)
            AND (@side::text IS NULL OR side = @side)
            AND (@type::text IS NULL OR type = @type)
            AND (@since::timestamptz IS NULL OR last_action_at > @since)
            """;
        var sql = $"""
            SELECT {Columns}, COUNT(*) OVER()
            FROM order_read_model
            WHERE {where}
            ORDER BY {column} {direction}, order_id
            LIMIT @limit OFFSET @offset;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("accountId", search.AccountId.Guid);
        command.Parameters.AddWithValue("ticker", NpgsqlDbType.Text, (object?)search.Ticker?.ToUpperInvariant() ?? DBNull.Value);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, (object?)search.State?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("side", NpgsqlDbType.Text, (object?)search.Side?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("type", NpgsqlDbType.Text, (object?)search.Type?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, (object?)search.UpdatedSince ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", IOrderSearch.PageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * IOrderSearch.PageSize);

        var items = new List<OrderView>();
        long total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new OrderView(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                Enum.Parse<OrderSide>(reader.GetString(3)),
                Enum.Parse<OrderType>(reader.GetString(4)),
                reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                Enum.Parse<OrderState>(reader.GetString(7)),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                reader.GetFieldValue<DateTimeOffset>(13),
                reader.GetFieldValue<DateTimeOffset>(14)));
            total = reader.GetInt64(15);
        }

        return new OrderPage(items, page, IOrderSearch.PageSize, total);
    }
}
