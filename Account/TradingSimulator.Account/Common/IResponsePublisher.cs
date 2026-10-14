namespace TradingSimulator.Account.Common;

public interface IResponsePublisher
{
    Task PublishAsync(PendingResponse pending, CancellationToken cancellationToken);
}
