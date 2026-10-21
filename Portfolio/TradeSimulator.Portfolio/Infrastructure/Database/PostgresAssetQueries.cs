using Moonad;
using Npgsql;
using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Features.GetAsset;
using TradeSimulator.Portfolio.Features.SearchAssets;

namespace TradeSimulator.Portfolio.Infrastructure.Database;

public sealed class PostgresAssetQueries(NpgsqlDataSource dataSource) : IAssetSearch, IAssetLookup
{
    private const string Columns = "asset_id, account_id, ticker, available, blocked, total";

    public async Task<AssetPage> SearchAsync(AssetSearchCriteria search, CancellationToken cancellationToken)
    {
        var column = AssetSortFields.Columns.GetValueOrDefault(search.SortBy, "ticker");
        var direction = search.Descending ? "DESC" : "ASC";
        var page = Math.Max(search.Page, 1);
        var sql = $"""
            SELECT {Columns}, COUNT(*) OVER()
            FROM asset_read_model
            WHERE account_id = @accountId
            ORDER BY {column} {direction}, ticker
            LIMIT @limit OFFSET @offset;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("accountId", search.AccountId.Guid);
        command.Parameters.AddWithValue("limit", IAssetSearch.PageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * IAssetSearch.PageSize);

        var items = new List<AssetItem>();
        long totalCount = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Map(reader));
            totalCount = reader.GetInt64(6);
        }

        return new AssetPage(items, page, IAssetSearch.PageSize, totalCount);
    }

    public async Task<Result<AssetItem, IPortfolioError>> GetAsync(AccountId accountId, Ticker ticker, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {Columns} FROM asset_read_model WHERE account_id = @accountId AND ticker = @ticker;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("accountId", accountId.Guid);
        command.Parameters.AddWithValue("ticker", ticker.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new AssetNotFound();

        return Map(reader);
    }

    private static AssetItem Map(NpgsqlDataReader reader) => new AssetReadModel(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5)).ToItem();
}
