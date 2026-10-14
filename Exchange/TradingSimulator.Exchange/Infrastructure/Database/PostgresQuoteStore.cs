using Npgsql;
using NpgsqlTypes;
using TradingSimulator.Exchange.Domain.ValueObjects;
using TradingSimulator.Exchange.Features.ImportInstruments;
using TradingSimulator.Exchange.Features.ImportQuotes;

namespace TradingSimulator.Exchange.Infrastructure.Database;

public sealed class PostgresQuoteStore(NpgsqlDataSource dataSource) : IQuoteStore
{
    public async Task<UpsertOutcome> UpsertAsync(IReadOnlyCollection<DailyQuote> quotes, CancellationToken cancellationToken)
    {
        // xmax = 0 identifies freshly inserted rows; identical rows are not rewritten.
        const string sql = """
            INSERT INTO quotes (ticker, trade_date, open_price, high_price, low_price, close_price)
            SELECT * FROM unnest(@tickers, @dates, @opens, @highs, @lows, @closes)
            ON CONFLICT (ticker, trade_date) DO UPDATE
                SET open_price = EXCLUDED.open_price, high_price = EXCLUDED.high_price,
                    low_price = EXCLUDED.low_price, close_price = EXCLUDED.close_price
                WHERE (quotes.open_price, quotes.high_price, quotes.low_price, quotes.close_price)
                      IS DISTINCT FROM (EXCLUDED.open_price, EXCLUDED.high_price, EXCLUDED.low_price, EXCLUDED.close_price)
            RETURNING (xmax = 0);
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("tickers", NpgsqlDbType.Array | NpgsqlDbType.Text)
            { Value = quotes.Select(q => q.Ticker.Value).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("dates", NpgsqlDbType.Array | NpgsqlDbType.Date)
            { Value = quotes.Select(q => q.TradeDate).ToArray() });
        command.Parameters.Add(Prices("opens", quotes.Select(q => q.Open.Value)));
        command.Parameters.Add(Prices("highs", quotes.Select(q => q.High.Value)));
        command.Parameters.Add(Prices("lows", quotes.Select(q => q.Low.Value)));
        command.Parameters.Add(Prices("closes", quotes.Select(q => q.Close.Value)));

        int inserted = 0, updated = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetBoolean(0))
                inserted++;
            else
                updated++;
        }

        return new UpsertOutcome(inserted, updated);
    }

    private static NpgsqlParameter Prices(string name, IEnumerable<decimal> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = values.ToArray() };
}
