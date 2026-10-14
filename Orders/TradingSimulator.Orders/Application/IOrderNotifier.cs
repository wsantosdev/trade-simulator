namespace TradingSimulator.Orders.Application;

public interface IOrderNotifier
{
    Task OrderCreatedAsync(OrderView order, CancellationToken cancellationToken);
    Task OrderUpdatedAsync(Guid accountId, OrderChanges changes, CancellationToken cancellationToken);
}
