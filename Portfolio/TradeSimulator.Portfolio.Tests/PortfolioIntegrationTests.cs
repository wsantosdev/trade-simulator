using System.Net.Http.Json;
using System.Text.Json;
using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Portfolio.Tests;

[Collection(InfrastructureCollection.Name)]
public sealed class PortfolioIntegrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ModuleFactory<Program> _factory = null!;
    private RabbitMqProbe _rabbit = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ModuleFactory<Program>(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Portfolio"] = await infrastructure.CreateDatabaseAsync(),
            ["ConnectionStrings:Redis"] = infrastructure.RedisConnection,
            ["RabbitMq:Uri"] = infrastructure.RabbitMqUri
        });
        _rabbit = await RabbitMqProbe.ConnectAsync(infrastructure.RabbitMqUri);
        await _rabbit.ListenAsync("trade-simulator.portfolio.reservations");
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task Deposit_command_from_rabbitmq_is_persisted_in_postgres()
    {
        var accountId = Guid.CreateVersion7();
        await DepositAsync(accountId, "PETR4", 100);

        var asset = await GetAssetAsync(accountId, "PETR4");
        Assert.Equal(100, asset.GetProperty("available").GetInt64());
        Assert.Equal(0, asset.GetProperty("blocked").GetInt64());
    }

    [Fact]
    public async Task Sell_order_blocks_assets_and_replies_through_rabbitmq()
    {
        var accountId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        await DepositAsync(accountId, "PETR4", 100);
        await _rabbit.WaitForQueueAsync("trade-simulator.portfolio.order.events");

        await PublishOrderSubmittedAsync(orderId, accountId, 40);

        var reply = await _rabbit.NextAsync(m => m.RoutingKey == "assets.blocked");
        Assert.Contains(orderId.ToString(), reply.Body);
        var asset = await GetAssetAsync(accountId, "PETR4");
        Assert.Equal(60, asset.GetProperty("available").GetInt64());
        Assert.Equal(40, asset.GetProperty("blocked").GetInt64());
    }

    [Fact]
    public async Task Sell_order_without_enough_assets_is_rejected_through_rabbitmq()
    {
        var accountId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        await _rabbit.WaitForQueueAsync("trade-simulator.portfolio.order.events");

        await PublishOrderSubmittedAsync(orderId, accountId, 10);

        var reply = await _rabbit.NextAsync(m => m.RoutingKey == "assets.block.failed");
        Assert.Contains(orderId.ToString(), reply.Body);
    }

    private async Task DepositAsync(Guid accountId, string ticker, long quantity)
    {
        await _rabbit.WaitForQueueAsync("trade-simulator.portfolio.asset.commands");
        await _rabbit.PublishAsync("trade-simulator.portfolio.command", "Deposit", JsonSerializer.Serialize(new
        {
            id = Guid.CreateVersion7(),
            eventType = "Deposit",
            @event = new { accountId, ticker, quantity }
        }));

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync($"/api/assets/{ticker}?accountId={accountId}");
            if (response.IsSuccessStatusCode)
                return;
            await Task.Delay(100);
        }

        Assert.Fail("Deposit was not applied within 20s.");
    }

    private Task PublishOrderSubmittedAsync(Guid orderId, Guid accountId, long quantity) =>
        _rabbit.PublishAsync("trade-simulator-order-commands", "OrderSubmitted", JsonSerializer.Serialize(new
        {
            id = Guid.CreateVersion7(),
            eventType = "OrderSubmitted",
            @event = new { orderId, accountId, ticker = "PETR4", side = "Sell", quantity }
        }));

    private async Task<JsonElement> GetAssetAsync(Guid accountId, string ticker) =>
        await _client.GetFromJsonAsync<JsonElement>($"/api/assets/{ticker}?accountId={accountId}");
}
