global using PortfolioResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Portfolio.Common.PortfolioCommandResult, TradingSimulator.Portfolio.Common.PortfolioCommandError>>;

global using AssetReservationResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Portfolio.Infrastructure.Messaging.AssetsBlocked, TradingSimulator.Portfolio.Infrastructure.Messaging.AssetBlockError>>;
