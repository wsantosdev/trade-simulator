using Moonad;
using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.Entities;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Common;

public interface IOrderStore
{
    Task<Result<Order, IOrderError>> SendAsync(Order order, CancellationToken cancellationToken);
    Task<Result<Order, IOrderError>> AppendAsync(Order order, CancellationToken cancellationToken);
    Task<Result<Order, IOrderError>> GetAsync(OrderId id, CancellationToken cancellationToken);
}
