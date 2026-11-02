using Moonad;
using MongoDB.Driver;
using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Features.GetAsset;
using TradeSimulator.Portfolio.Features.SearchAssets;

namespace TradeSimulator.Portfolio.Infrastructure.ReadModel;

public sealed class MongoAssetReadModel : IAssetSearch, IAssetLookup
{
    private readonly IMongoCollection<AssetDocument> _assets;

    public MongoAssetReadModel(IMongoDatabase database)
    {
        MongoSetup.Initialize();
        _assets = database.GetCollection<AssetDocument>("assets");
    }

    public Task EnsureIndexesAsync(CancellationToken cancellationToken) =>
        _assets.Indexes.CreateOneAsync(
            new CreateIndexModel<AssetDocument>(Builders<AssetDocument>.IndexKeys.Ascending(a => a.AccountId).Ascending(a => a.Ticker)),
            cancellationToken: cancellationToken);

    public async Task<AssetPage> SearchAsync(AssetSearchCriteria search, CancellationToken cancellationToken)
    {
        var filter = Builders<AssetDocument>.Filter.Eq(a => a.AccountId, search.AccountId.Guid);
        var field = AssetSortFields.Columns.GetValueOrDefault(search.SortBy, "ticker");
        var sort = search.Descending
            ? Builders<AssetDocument>.Sort.Descending(field).Ascending("ticker")
            : Builders<AssetDocument>.Sort.Ascending(field).Ascending("ticker");
        var page = Math.Max(search.Page, 1);

        var total = await _assets.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var documents = await _assets.Find(filter).Sort(sort)
            .Skip((page - 1) * IAssetSearch.PageSize).Limit(IAssetSearch.PageSize)
            .ToListAsync(cancellationToken);
        return new AssetPage(documents.ConvertAll(d => d.ToItem()), page, IAssetSearch.PageSize, total);
    }

    public async Task<Result<AssetItem, IPortfolioError>> GetAsync(AccountId accountId, Ticker ticker, CancellationToken cancellationToken)
    {
        var document = await _assets.Find(a => a.AccountId == accountId.Guid && a.Ticker == ticker.Value).FirstOrDefaultAsync(cancellationToken);
        if (document is null)
            return new AssetNotFound();

        return document.ToItem();
    }

    // Applies a document only if it is newer than the stored one, so replays and out-of-order projections are harmless.
    public async Task UpsertAsync(AssetReadModel model, int version, CancellationToken cancellationToken)
    {
        try
        {
            await _assets.ReplaceOneAsync(
                a => a.Id == model.AssetId && a.Version < version,
                AssetDocument.From(model, version),
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
        }
    }

    // An emptied asset leaves the portfolio, unless a newer version already re-created it.
    public Task RemoveAsync(Guid assetId, int version, CancellationToken cancellationToken) =>
        _assets.DeleteOneAsync(a => a.Id == assetId && a.Version <= version, cancellationToken);
}
