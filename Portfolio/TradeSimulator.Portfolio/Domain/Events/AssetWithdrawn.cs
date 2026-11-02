using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Domain.Events;

public sealed record AssetWithdrawn(AssetId AssetId, Quantity Quantity, DateTimeOffset OccurredAt, Guid ReservationId, Guid OperationId)
    : AssetEvent(AssetId, OccurredAt);
