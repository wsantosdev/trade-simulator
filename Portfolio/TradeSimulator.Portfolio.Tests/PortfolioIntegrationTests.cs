using System.Net;
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
            ["ConnectionStrings:Mongo"] = infrastructure.MongoConnection,
            ["Mongo:Database"] = infrastructure.CreateMongoDatabaseName(),
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

        var asset = await GetAssetAsync(accountId, "PETR4", available: 100);
        Assert.Equal(100, asset.GetProperty("available").GetInt64());
        Assert.Equal(0, asset.GetProperty("blocked").GetInt64());
    }

    [Fact]
    public async Task Sell_order_reservation_blocks_assets_synchronously()
    {
        var accountId = Guid.CreateVersion7();
        await DepositAsync(accountId, "PETR4", 100);

        var response = await ReserveAsync(accountId, Guid.CreateVersion7(), 40);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = await GetAssetAsync(accountId, "PETR4", available: 60);
        Assert.Equal(60, asset.GetProperty("available").GetInt64());
        Assert.Equal(40, asset.GetProperty("blocked").GetInt64());
    }

    [Fact]
    public async Task Sell_order_without_position_is_rejected_with_422()
    {
        var response = await ReserveAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), 10);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AssetNotFound", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Concurrent_sell_orders_never_block_more_than_the_position()
    {
        var accountId = Guid.CreateVersion7();
        await DepositAsync(accountId, "PETR4", 100);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ReserveAsync(accountId, Guid.CreateVersion7(), 30)));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var asset = await GetAssetAsync(accountId, "PETR4", available: 10);
        Assert.Equal(10, asset.GetProperty("available").GetInt64());
    }

    [Fact]
    public async Task Released_reservation_returns_the_quantity()
    {
        var accountId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        await DepositAsync(accountId, "PETR4", 100);
        await ReserveAsync(accountId, orderId, 40);

        var released = await _client.DeleteAsync($"/api/order-reservations/{orderId}");

        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        var asset = await GetAssetAsync(accountId, "PETR4", available: 100);
        Assert.Equal(100, asset.GetProperty("available").GetInt64());
    }

    private Task<HttpResponseMessage> ReserveAsync(Guid accountId, Guid orderId, long quantity) =>
        _client.PostAsJsonAsync("/api/order-reservations", new { orderId, accountId, ticker = "PETR4", quantity });

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

    // The read model is eventually consistent: polls until the asset shows the expected available quantity.
    private async Task<JsonElement> GetAssetAsync(Guid accountId, string ticker, long? available = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var response = await _client.GetAsync($"/api/assets/{ticker}?accountId={accountId}");
            if (response.IsSuccessStatusCode)
            {
                var asset = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (available is null || asset.GetProperty("available").GetInt64() == available || DateTime.UtcNow > deadline)
                    return asset;
            }
            else if (DateTime.UtcNow > deadline)
                return await _client.GetFromJsonAsync<JsonElement>($"/api/assets/{ticker}?accountId={accountId}");

            await Task.Delay(100);
        }
    }
}
