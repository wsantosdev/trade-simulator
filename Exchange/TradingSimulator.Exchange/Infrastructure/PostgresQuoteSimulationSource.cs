using Moonad;
using Npgsql;
using TradingSimulator.Exchange.Application;
using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Infrastructure;

public sealed class PostgresQuoteSimulationSource(NpgsqlDataSource dataSource) : IQuoteSimulationSource
{
    // The newest row gives the day to simulate, LEAD() the close before it, and the subquery the bounds of the whole series.
    private const string Sql = """
        SELECT q.open_price, q.high_price, q.low_price, q.close_price,
               LEAD(q.close_price) OVER (ORDER BY q.trade_date DESC) AS previous_close,
               bounds.lowest, bounds.highest
        FROM quotes q
        CROSS JOIN (SELECT MIN(low_price) AS lowest, MAX(high_price) AS highest FROM quotes WHERE ticker = @ticker) bounds
        WHERE q.ticker = @ticker
        ORDER BY q.trade_date DESC
        LIMIT 1;
        """;

    public async Task<Result<SimulationParameters, IExchangeError>> LoadAsync(
        Ticker ticker, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(Sql);
        command.Parameters.AddWithValue("ticker", ticker.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new NoQuoteHistory();

        var open = reader.GetDecimal(0);
        var close = reader.GetDecimal(3);
        var previousClose = reader.IsDBNull(4) ? open : reader.GetDecimal(4);
        return SimulationParameters.TryCreate(
            ticker, open, previousClose, close,
            dayHigh: reader.GetDecimal(1), dayLow: reader.GetDecimal(2),
            floor: reader.GetDecimal(5), ceiling: reader.GetDecimal(6));
    }
}
