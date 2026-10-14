global using PortfolioResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Portfolio.Processors.PortfolioCommands.PortfolioCommandResult, TradingSimulator.Portfolio.Processors.PortfolioCommands.PortfolioCommandError>>;

global using AssetReservationResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Portfolio.Processors.PortfolioCommands.AssetsBlocked, TradingSimulator.Portfolio.Processors.PortfolioCommands.AssetBlockError>>;
