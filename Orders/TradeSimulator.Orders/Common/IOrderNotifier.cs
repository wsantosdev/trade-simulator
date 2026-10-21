
namespace TradeSimulator.Orders.Common;

public interface IOrderNotifier
{
    Task OrderCreatedAsync(OrderView order, CancellationToken cancellationToken);
    Task OrderUpdatedAsync(Guid accountId, OrderChanges changes, CancellationToken cancellationToken);
}
