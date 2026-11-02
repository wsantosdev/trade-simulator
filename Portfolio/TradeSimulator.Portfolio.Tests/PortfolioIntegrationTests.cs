using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TradeSimulator.Portfolio.Infrastructure.Messaging;
using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Portfolio.Tests;

[Collection(InfrastructureCollection.Name)]
public sealed class PortfolioIntegrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ModuleFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private RabbitMqProbe _rabbit = null!;

    public async Task InitializeAsync()
    {
        _factory = new ModuleFactory<Program>(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Portfolio"] = await infrastructure.CreateDatabaseAsync(),
            ["ConnectionStrings:Mongo"] = infrastructure.MongoConnection,
            ["Mongo:Database"] = infrastructure.CreateMongoDatabaseName(),
            ["RabbitMq:Uri"] = infrastructure.RabbitMqUri
        });
        _client = _factory.CreateClient();
        _rabbit = await RabbitMqProbe.ConnectAsync(infrastructure.RabbitMqUri);
        await _rabbit.WaitForQueueAsync(OrderEventConsumer.Queue);
    }

    public async Task DisposeAsync()
    {
        await _rabbit.DisposeAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Deposit_is_persisted_and_projected()
    {
        var accountId = Guid.CreateVersion7();
        await DepositAsync(accountId, "PETR4", 100);

        var asset = await GetAssetAsync(accountId, "PETR4", available: 100);
        Assert.Equal(100, asset.GetProperty("available").GetInt64());
        Assert.Equal(0, asset.GetProperty("blocked").GetInt64());
    }

    [Fact]
    public async Task Reservation_blocks_assets_synchronously()
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
    public async Task Reservation_without_position_is_rejected_with_404()
    {
        var response = await ReserveAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), 10);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AssetNotFound", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Concurrent_reservations_never_block_more_than_the_position()
    {
        var accountId = Guid.CreateVersion7();
        await DepositAsync(accountId, "PETR4", 100);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ReserveAsync(accountId, Guid.CreateVersion7(), 30)));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var asset = await GetAssetAsync(accountId, "PETR4", available: 10);
        Assert.Equal(10, asset.GetProperty("available").GetInt64());
    }

    [Theory]
    [InlineData("OrderCancelled")]
    [InlineData("OrderRejected")]
    [InlineData("OrderScheduleCancelled")]
    public async Task Order_closing_events_return_the_quantity(string eventType)
    {
        var accountId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        await DepositAsync(accountId, "PETR4", 100);
        await ReserveAsync(accountId, reservationId, 40);

        await _rabbit.PublishAsync(OrderEventMessages.Exchange, eventType, OrderEventMessages.Cancel(eventType, reservationId));

        var asset = await GetAssetAsync(accountId, "PETR4", available: 100);
        Assert.Equal(0, asset.GetProperty("blocked").GetInt64());
    }

    [Fact]
    public async Task Fills_withdraw_the_executed_quantity_and_the_final_one_returns_the_leftover()
    {
        var accountId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        await DepositAsync(accountId, "PETR4", 100);
        await ReserveAsync(accountId, reservationId, 40);
        var eventId = Guid.CreateVersion7();

        // The same event delivered twice is settled once.
        for (var i = 0; i < 2; i++)
            await _rabbit.PublishAsync(OrderEventMessages.Exchange, "OrderPartiallyFilled",
                OrderEventMessages.Fill("OrderPartiallyFilled", reservationId, 10, 5m, eventId));
        var partial = await GetAssetAsync(accountId, "PETR4", available: 60);
        Assert.Equal(30, partial.GetProperty("blocked").GetInt64());

        await _rabbit.PublishAsync(OrderEventMessages.Exchange, "OrderFilled",
            OrderEventMessages.Fill("OrderFilled", reservationId, 20, 5m));
        var asset = await GetAssetAsync(accountId, "PETR4", available: 70);
        Assert.Equal(0, asset.GetProperty("blocked").GetInt64());
    }

    private Task<HttpResponseMessage> ReserveAsync(Guid accountId, Guid reservationId, long quantity) =>
        _client.PostAsJsonAsync("/api/assets/reservations", new { reservationId, accountId, ticker = "PETR4", quantity });

    private async Task DepositAsync(Guid accountId, string ticker, long quantity)
    {
        var response = await _client.PostAsJsonAsync("/api/assets/deposits", new { accountId, ticker, quantity });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
