global using PortfolioResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Portfolio.Application.PortfolioCommandResult, TradingSimulator.Portfolio.Application.PortfolioCommandError>>;

global using AssetReservationResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Portfolio.Application.AssetsBlocked, TradingSimulator.Portfolio.Application.AssetBlockError>>;
