using Microsoft.AspNetCore.SignalR;

namespace TradingSimulator.Orders.Api.Hubs;

public sealed class OrdersHub : Hub
{
    public const string Route = "/hubs/orders";

    public static string GroupFor(Guid accountId) => $"account-{accountId:D}";

    public Task SubscribeAccount(Guid accountId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(accountId));

    public Task UnsubscribeAccount(Guid accountId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(accountId));
}
