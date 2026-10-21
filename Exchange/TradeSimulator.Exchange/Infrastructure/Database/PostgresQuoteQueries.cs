using Npgsql;
using TradeSimulator.Exchange.Domain.ValueObjects;
using TradeSimulator.Exchange.Features.QueryQuotes;

namespace TradeSimulator.Exchange.Infrastructure.Database;

public sealed class PostgresQuoteQueries(NpgsqlDataSource dataSource) : IQuoteQueries
{
    public async Task<IReadOnlyList<QuoteItem>> HistoryAsync(
        Ticker ticker, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ticker, trade_date, open_price, high_price, low_price, close_price
            FROM quotes
            WHERE ticker = @ticker
              AND (@from::date IS NULL OR trade_date >= @from)
              AND (@to::date IS NULL OR trade_date <= @to)
            ORDER BY trade_date;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("ticker", ticker.Value);
        command.Parameters.AddWithValue("from", from.HasValue ? from.Value : DBNull.Value);
        command.Parameters.AddWithValue("to", to.HasValue ? to.Value : DBNull.Value);

        var items = new List<QuoteItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new QuoteItem(
                reader.GetString(0), DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5)));
        }

        return items;
    }
}
