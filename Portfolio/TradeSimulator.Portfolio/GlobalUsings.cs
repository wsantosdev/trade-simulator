global using PortfolioResponse = TradeSimulator.Commons.EventEnvelope<Moonad.Result<TradeSimulator.Portfolio.Processors.PortfolioCommands.PortfolioCommandResult, TradeSimulator.Portfolio.Processors.PortfolioCommands.PortfolioCommandError>>;

global using AssetReservationResponse = TradeSimulator.Commons.EventEnvelope<Moonad.Result<TradeSimulator.Portfolio.Processors.PortfolioCommands.AssetsBlocked, TradeSimulator.Portfolio.Processors.PortfolioCommands.AssetBlockError>>;
