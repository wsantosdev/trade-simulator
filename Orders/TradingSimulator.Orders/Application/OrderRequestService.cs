using Moonad;
using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Application;

public sealed class OrderRequestService(IOrderStore store, IOrderNotifier notifier, TimeProvider clock)
{
    public Task<Result<Order, IOrderError>> RequestCancelAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken) =>
        ExecuteAsync(accountId, orderId, (order, now) => order.RequestCancel(now), cancellationToken);

    public Task<Result<Order, IOrderError>> RequestReplaceAsync(Guid accountId, Guid orderId, decimal price, CancellationToken cancellationToken) =>
        ExecuteAsync(accountId, orderId, (order, now) => order.RequestReplace(price, now), cancellationToken);

    private async Task<Result<Order, IOrderError>> ExecuteAsync(
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
