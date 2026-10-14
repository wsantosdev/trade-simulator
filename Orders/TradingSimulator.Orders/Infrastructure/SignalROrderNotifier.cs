using Microsoft.AspNetCore.SignalR;
using TradingSimulator.Orders.Api.Hubs;
using TradingSimulator.Orders.Application;

namespace TradingSimulator.Orders.Infrastructure;

public sealed class SignalROrderNotifier(IHubContext<OrdersHub> hub, ILogger<SignalROrderNotifier> logger) : IOrderNotifier
{
    public Task OrderCreatedAsync(OrderView order, CancellationToken cancellationToken) =>
        SendAsync(order.AccountId, "OrderCreated", order, cancellationToken);

    public Task OrderUpdatedAsync(Guid accountId, OrderChanges changes, CancellationToken cancellationToken) =>
        SendAsync(accountId, "OrderUpdated", changes, cancellationToken);

    // Real-time delivery is best effort: the UI recovers missed updates by timestamp on reconnect.
    private async Task SendAsync(Guid accountId, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(OrdersHub.GroupFor(accountId)).SendAsync(method, payload, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not push {Method} to account {AccountId}.", method, accountId);
        }
    }
}
