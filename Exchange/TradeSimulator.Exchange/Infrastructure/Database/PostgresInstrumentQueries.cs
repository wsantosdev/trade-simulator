using Moonad;
using Npgsql;
using TradeSimulator.Exchange.Domain;
using TradeSimulator.Exchange.Domain.ValueObjects;
using TradeSimulator.Exchange.Features.GetInstrument;
using TradeSimulator.Exchange.Features.SearchInstruments;
using TradeSimulator.Exchange.Features.Shared;

namespace TradeSimulator.Exchange.Infrastructure.Database;

public sealed class PostgresInstrumentQueries(NpgsqlDataSource dataSource) : IInstrumentSearch, IInstrumentLookup
{
    public async Task<InstrumentPage> SearchAsync(string? text, int page, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        var term = text?.Trim();
        const string sql = """
            SELECT instrument_id, ticker, name, COUNT(*) OVER()
            FROM instruments
            WHERE @term::text IS NULL
               OR ticker LIKE upper(@term) || '%'
               OR name ILIKE '%' || @term || '%'
            ORDER BY ticker
            LIMIT @limit OFFSET @offset;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("term", string.IsNullOrEmpty(term) ? DBNull.Value : EscapeLike(term));
        command.Parameters.AddWithValue("limit", IInstrumentSearch.PageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * IInstrumentSearch.PageSize);

        var items = new List<InstrumentItem>();
        long totalCount = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new InstrumentItem(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
            totalCount = reader.GetInt64(3);
        }

        return new InstrumentPage(items, page, IInstrumentSearch.PageSize, totalCount);
    }

    public async Task<Result<InstrumentItem, IExchangeError>> GetAsync(Ticker ticker, CancellationToken cancellationToken)
    {
        const string sql = "SELECT instrument_id, ticker, name FROM instruments WHERE ticker = @ticker;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("ticker", ticker.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new InstrumentNotFound();

        return new InstrumentItem(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
    }

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
