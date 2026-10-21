using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Orders.Tests;

[Collection(InfrastructureCollection.Name)]
public sealed class OrdersIntegrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ModuleFactory<Program> _factory = null!;
    private RabbitMqProbe _rabbit = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ModuleFactory<Program>(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Orders"] = await infrastructure.CreateDatabaseAsync(),
            ["RabbitMq:Uri"] = infrastructure.RabbitMqUri
        });
        _rabbit = await RabbitMqProbe.ConnectAsync(infrastructure.RabbitMqUri);
        await _rabbit.ListenAsync("trade-simulator-order-commands");
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task Submitted_order_is_persisted_in_postgres_and_published_through_the_outbox_to_rabbitmq()
    {
        var accountId = Guid.CreateVersion7();
        var (orderId, _) = await SubmitAsync(accountId);

        var history = await _client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/history?accountId={accountId}");
        Assert.Equal("OrderSubmitted", history[0].GetProperty("eventType").GetString());

        var published = await _rabbit.NextAsync(m => m.RoutingKey == "OrderSubmitted");
        Assert.Contains(orderId.ToString(), published.Body);
        Assert.NotNull(published.Headers);
        Assert.True(published.Headers!.ContainsKey("traceparent"));
    }

    [Fact]
    public async Task Funds_reservation_failure_received_from_rabbitmq_rejects_the_order()
    {
        var accountId = Guid.CreateVersion7();
        var (orderId, _) = await SubmitAsync(accountId);
        await _rabbit.WaitForQueueAsync("trade-simulator.orders.account.events");

        await _rabbit.PublishAsync("trade-simulator.account.reservations", "funds.reservation.failed", JsonSerializer.Serialize(new
        {
            id = Guid.CreateVersion7(),
            eventType = "FundsReservationResult",
            @event = new
            {
                isError = true,
                error = new { orderId, accountId, kind = "InsufficientFunds", message = "no balance" }
            }
        }));

        await Eventually(async () =>
        {
            var found = await _client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}/history?accountId={accountId}");
            return found.EnumerateArray().Any(e => e.GetProperty("eventType").GetString()!.Contains("Rejected"));
        });
    }

    private async Task<(Guid OrderId, JsonElement Body)> SubmitAsync(Guid accountId)
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            accountId, ticker = "PETR4", side = "Buy", type = "Limit", quantity = 10, price = 20m,
            requestHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body);
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met within 20s.");
            await Task.Delay(100);
        }
    }
}
