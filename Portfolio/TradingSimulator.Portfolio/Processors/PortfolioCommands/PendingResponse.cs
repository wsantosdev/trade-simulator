namespace TradingSimulator.Portfolio.Processors.PortfolioCommands;

// Exchange is null for the portfolio events exchange (the default of the response publisher).
public sealed record PendingResponse(long Position, string RoutingKey, string Payload, string? Exchange = null, string? TraceParent = null);
