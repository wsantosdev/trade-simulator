using System.Text.Json;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

// Client commands: malformed messages are answered with a failure instead of being dropped.
public sealed class PortfolioCommandMessageHandler(
    PortfolioCommandProcessor processor,
    ILogger<PortfolioCommandMessageHandler> logger)
{
    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = EnvelopeReader.TryRead(body, logger);
        if (envelope is null)
        {
            await processor.RejectMalformedAsync(Guid.Empty, new MalformedCommand(), body, cancellationToken);
            return;
        }

        if (!EnvelopeReader.HasValidId(envelope))
        {
            await processor.RejectMalformedAsync(envelope.Id, new InvalidEnvelopeId(), body, cancellationToken);
            return;
        }

        AssetCommand? command;
        try
        {
            command = Parse(envelope.EventType, envelope.Event);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Command {EventType} {EnvelopeId} has an invalid payload.", envelope.EventType, envelope.Id);
            await processor.RejectMalformedAsync(envelope.Id, new MalformedCommand(), body, cancellationToken);
            return;
        }

        if (command is null)
        {
            await processor.RejectMalformedAsync(envelope.Id, new UnsupportedCommand(), body, cancellationToken);
            return;
        }

        await processor.ExecuteAsync(envelope.Id, command, cancellationToken);
    }

    private static AssetCommand? Parse(string eventType, JsonElement payload)
    {
        switch (eventType)
        {
            case PortfolioEventTypes.Deposit:
                var deposit = EnvelopeReader.Read<DepositMessage>(payload);
                return new AssetCommand(AssetOperation.Deposit, deposit.AccountId, deposit.Ticker, deposit.Quantity);
            case PortfolioEventTypes.Withdraw:
                var withdraw = EnvelopeReader.Read<WithdrawMessage>(payload);
                return new AssetCommand(AssetOperation.Withdraw, withdraw.AccountId, withdraw.Ticker, withdraw.Quantity);
            case PortfolioEventTypes.Block:
                var block = EnvelopeReader.Read<BlockMessage>(payload);
                return new AssetCommand(AssetOperation.Block, block.AccountId, block.Ticker, block.Quantity);
            case PortfolioEventTypes.Unblock:
                var unblock = EnvelopeReader.Read<UnblockMessage>(payload);
                return new AssetCommand(AssetOperation.Unblock, unblock.AccountId, unblock.Ticker, unblock.Quantity);
            default:
                return null;
        }
    }
}
