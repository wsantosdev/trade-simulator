using MongoDB.Driver;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain.ValueObjects;
using TradeSimulator.Orders.Features.QueryOrders;

namespace TradeSimulator.Orders.Infrastructure.ReadModel;

public sealed class MongoOrderReadModel(IMongoDatabase database) : IOrderSearch, IScheduledOrders
{
    private readonly IMongoCollection<OrderDocument> _orders = database.GetCollection<OrderDocument>("orders");

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        var keys = Builders<OrderDocument>.IndexKeys;
        await _orders.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<OrderDocument>(keys.Ascending(o => o.AccountId).Descending(o => o.LastActionAt)),
            new CreateIndexModel<OrderDocument>(keys.Ascending(o => o.AccountId).Descending(o => o.SentAt)),
            new CreateIndexModel<OrderDocument>(keys.Ascending(o => o.State).Ascending(o => o.SentAt))
        ], cancellationToken);
    }

    public async Task<OrderPage> SearchAsync(OrderSearch search, CancellationToken cancellationToken)
    {
        var f = Builders<OrderDocument>.Filter;
        var filter = f.Eq(o => o.AccountId, search.AccountId.Guid);
        if (search.Ticker is not null)
            filter &= f.Eq(o => o.Ticker, search.Ticker.ToUpperInvariant());
        if (search.State is { } state)
            filter &= f.Eq(o => o.State, state);
        if (search.Side is { } side)
            filter &= f.Eq(o => o.Side, side);
        if (search.Type is { } type)
            filter &= f.Eq(o => o.Type, type);
        if (search.UpdatedSince is { } since)
            filter &= f.Gt(o => o.LastActionAt, since);

        var field = OrderSortFields.Columns.GetValueOrDefault(search.SortBy, "sentAt");
        var sort = search.Descending
            ? Builders<OrderDocument>.Sort.Descending(field).Ascending("_id")
            : Builders<OrderDocument>.Sort.Ascending(field).Ascending("_id");
        var page = Math.Max(search.Page, 1);

        var total = await _orders.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var documents = await _orders.Find(filter).Sort(sort)
            .Skip((page - 1) * IOrderSearch.PageSize).Limit(IOrderSearch.PageSize)
            .ToListAsync(cancellationToken);
        return new OrderPage(documents.ConvertAll(d => d.ToView()), page, IOrderSearch.PageSize, total);
    }

    public async Task<IReadOnlyList<OrderId>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var ids = await _orders.Find(o => o.State == Domain.Enums.OrderState.ScheduledDispatching)
            .SortBy(o => o.SentAt).ThenBy(o => o.Id).Limit(limit)
            .Project(o => o.Id).ToListAsync(cancellationToken);
        return ids.ConvertAll(id => OrderId.TryCreate(id).ResultValue);
    }

    // Applies a document only if it is newer than the stored one, so replays and out-of-order projections are harmless.
    public async Task UpsertAsync(OrderView view, int version, CancellationToken cancellationToken)
    {
        try
        {
            await _orders.ReplaceOneAsync(
                o => o.Id == view.Id && o.Version < version,
                OrderDocument.From(view, version),
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
        }
    }

    public Task RemoveAsync(Guid orderId, CancellationToken cancellationToken) =>
        _orders.DeleteOneAsync(o => o.Id == orderId, cancellationToken);
}
