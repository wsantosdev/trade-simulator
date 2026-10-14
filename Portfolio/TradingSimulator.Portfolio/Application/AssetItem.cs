namespace TradingSimulator.Portfolio.Application;

// Model sent to the UI (HTTP and SignalR): the asset and account ids stay in the database.
public sealed record AssetItem(string Ticker, long Available, long Blocked, long Total);
