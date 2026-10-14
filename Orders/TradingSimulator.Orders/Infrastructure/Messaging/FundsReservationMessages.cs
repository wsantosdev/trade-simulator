using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Infrastructure.Messaging;

public sealed record FundsReserved(OrderId OrderId, AccountId AccountId, decimal Amount);

// Kind is kept as text so that new Account error kinds never make the message undeliverable.
public sealed record FundsReservationError(OrderId OrderId, AccountId AccountId, string Kind, string? Message);

public sealed record AssetsBlocked(OrderId OrderId, AccountId AccountId, string Ticker, long Quantity);

// Kind is kept as text (InsufficientQuantity | AssetNotFound | InvalidOrder) so new Portfolio kinds stay deliverable.
public sealed record AssetBlockError(OrderId OrderId, AccountId AccountId, string Ticker, string Kind, string? Message);
