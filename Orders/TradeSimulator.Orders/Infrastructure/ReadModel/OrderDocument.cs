using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain.Enums;

namespace TradeSimulator.Orders.Infrastructure.ReadModel;

// Order as stored in the read model (MongoDB collection "orders").
public sealed class OrderDocument
{
    static OrderDocument()
    {
        MongoSetup.Initialize();

        ConventionRegistry.Register(
            nameof(OrderDocument), new ConventionPack { new CamelCaseElementNameConvention() }, type => type == typeof(OrderDocument));
    }

    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Ticker { get; set; } = "";
    [BsonRepresentation(BsonType.String)] public OrderSide Side { get; set; }
    [BsonRepresentation(BsonType.String)] public OrderType Type { get; set; }
    public long Quantity { get; set; }
    [BsonRepresentation(BsonType.Decimal128)] public decimal? Price { get; set; }
    [BsonRepresentation(BsonType.String)] public OrderState State { get; set; }
    public long ExecutedQuantity { get; set; }
    public long RemainingQuantity { get; set; }
    [BsonRepresentation(BsonType.Decimal128)] public decimal? AveragePrice { get; set; }
    [BsonRepresentation(BsonType.Decimal128)] public decimal? LastPrice { get; set; }
    [BsonRepresentation(BsonType.DateTime)] public DateTimeOffset? LastTradeAt { get; set; }
    [BsonRepresentation(BsonType.DateTime)] public DateTimeOffset SentAt { get; set; }
    [BsonRepresentation(BsonType.DateTime)] public DateTimeOffset LastActionAt { get; set; }
    public int Version { get; set; }

    public static OrderDocument From(OrderView view, int version) => new()
    {
        Id = view.Id, AccountId = view.AccountId, Ticker = view.Ticker, Side = view.Side, Type = view.Type,
        Quantity = view.Quantity, Price = view.Price, State = view.State, ExecutedQuantity = view.ExecutedQuantity,
        RemainingQuantity = view.RemainingQuantity, AveragePrice = view.AveragePrice, LastPrice = view.LastPrice,
        LastTradeAt = view.LastTradeAt, SentAt = view.SentAt, LastActionAt = view.LastActionAt, Version = version
    };

    public OrderView ToView() => new(
        Id, AccountId, Ticker, Side, Type, Quantity, Price, State, ExecutedQuantity, RemainingQuantity,
        AveragePrice, LastPrice, LastTradeAt, SentAt, LastActionAt);
}
