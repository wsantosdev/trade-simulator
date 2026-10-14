using TradingSimulator.Orders.Common;
using Moonad;
using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.Entities;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Features.Shared;

// Shared by the use cases in which an account owner acts on one of their own orders.
public sealed class OrderModifier(IOrderStore store, IOrderNotifier notifier, TimeProvider clock)
{
    public async Task<Result<Order, IOrderError>> ModifyAsync(
        Guid accountId,
        Guid orderId,
        Func<Order, DateTimeOffset, Result<Order, IOrderError>> action,
        CancellationToken cancellationToken)
    {
        var account = AccountId.TryCreate(accountId);
        if (account.IsError)
            return Result<Order, IOrderError>.Error(account.ErrorValue);
        var id = OrderId.TryCreate(orderId);
        if (id.IsError)
            return Result<Order, IOrderError>.Error(id.ErrorValue);

        var loaded = await store.GetAsync(id.ResultValue, cancellationToken);
        if (loaded.IsError)
            return loaded;

        var order = loaded.ResultValue;
        if (order.AccountId != account.ResultValue)
            return new OrderNotFound();

        var before = OrderView.From(order);
        var applied = action(order, clock.GetUtcNow());
        if (applied.IsError)
            return applied;

        var saved = await store.AppendAsync(applied.ResultValue, cancellationToken);
        if (saved.IsError)
            return saved;

        await notifier.OrderUpdatedAsync(
            accountId, OrderChanges.Between(before, OrderView.From(saved.ResultValue)), cancellationToken);
        return saved;
    }
}
