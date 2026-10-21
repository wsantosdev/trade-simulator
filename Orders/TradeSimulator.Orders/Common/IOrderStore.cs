using Moonad;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.Entities;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Common;

public interface IOrderStore
{
    Task<Result<Order, IOrderError>> SendAsync(Order order, CancellationToken cancellationToken);
    Task<Result<Order, IOrderError>> AppendAsync(Order order, CancellationToken cancellationToken);
    Task<Result<Order, IOrderError>> GetAsync(OrderId id, CancellationToken cancellationToken);
}
