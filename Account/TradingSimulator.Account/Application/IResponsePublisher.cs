namespace TradingSimulator.Account.Application;

public interface IResponsePublisher
{
    Task PublishAsync(PendingResponse pending, CancellationToken cancellationToken);
}
