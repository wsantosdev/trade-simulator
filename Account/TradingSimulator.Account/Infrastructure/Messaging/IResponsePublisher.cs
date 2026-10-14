namespace TradingSimulator.Account.Infrastructure.Messaging;

public interface IResponsePublisher
{
    Task PublishAsync(PendingResponse pending, CancellationToken cancellationToken);
}
