using Moonad;
using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Application;

public interface IOrderStore
{
    Task<Result<Order, IOrderError>> SendAsync(Order order, CancellationToken cancellationToken);
    Task<Result<Order, IOrderError>> AppendAsync(Order order, CancellationToken cancellationToken);
    Task<Result<Order, IOrderError>> GetAsync(OrderId id, CancellationToken cancellationToken);
}
