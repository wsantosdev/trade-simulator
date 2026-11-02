using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradeSimulator.Orders.Domain.Entities;
using TradeSimulator.Orders.Processors;
using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Orders.Tests;

[Collection(InfrastructureCollection.Name)]
public sealed class OrdersIntegrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ModuleFactory<Program> _factory = null!;
    private RabbitMqProbe _rabbit = null!;
    private HttpClient _client = null!;
    private readonly FakeGateway _gateway = new();

    public async Task InitializeAsync()
    {
        _factory = new ModuleFactory<Program>(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Orders"] = await infrastructure.CreateDatabaseAsync(),
            ["ConnectionStrings:Mongo"] = infrastructure.MongoConnection,
            ["Mongo:Database"] = infrastructure.CreateMongoDatabaseName(),
            ["RabbitMq:Uri"] = infrastructure.RabbitMqUri
        });
        _rabbit = await RabbitMqProbe.ConnectAsync(infrastructure.RabbitMqUri);
        await _rabbit.ListenAsync("trade-simulator-order-commands");
        _client = _factory.WithWebHostBuilder(host => host.ConfigureServices(services =>
        {
            services.RemoveAll<IReservationGateway>();
            services.AddSingleton<IReservationGateway>(_gateway);
        })).CreateClient();
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
    public async Task Projector_feeds_the_mongo_read_model_with_accepted_orders_only()
    {
        var accountId = Guid.CreateVersion7();
        var (orderId, _) = await SubmitAsync(accountId);
        _gateway.Outcome = ReservationOutcome.Rejected("InsufficientFunds");
        await Post(accountId);

        var listed = new List<string>();
        for (var attempt = 0; attempt < 50 && listed.Count == 0; attempt++)
        {
            await Task.Delay(100);
            var page = await _client.GetFromJsonAsync<JsonElement>($"/api/orders?accountId={accountId}");
            listed = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
        }

        Assert.Equal([orderId.ToString()], listed);
    }

    [Fact]
    public async Task Rejected_reservation_answers_422_and_the_order_is_not_listed()
    {
        _gateway.Outcome = ReservationOutcome.Rejected("InsufficientFunds");
        var accountId = Guid.CreateVersion7();

        var response = await Post(accountId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var orders = await _client.GetFromJsonAsync<JsonElement>($"/api/orders?accountId={accountId}");
        Assert.DoesNotContain(accountId.ToString(), orders.ToString());
        Assert.Single(_gateway.Reserved);
    }

    [Fact]
    public async Task Unavailable_reservation_service_answers_503()
    {
        _gateway.Outcome = ReservationOutcome.ServiceUnavailable("Account");

        var response = await Post(Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class FakeGateway : IReservationGateway
    {
        public ReservationOutcome Outcome { get; set; } = ReservationOutcome.Ok();
        public List<Order> Reserved { get; } = [];

        public Task<ReservationOutcome> ReserveAsync(Order order, CancellationToken cancellationToken)
        {
            Reserved.Add(order);
            return Task.FromResult(Outcome);
        }

        public Task ReleaseAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private Task<HttpResponseMessage> Post(Guid accountId) =>
        _client.PostAsJsonAsync("/api/orders", new
        {
            accountId, ticker = "PETR4", side = "Buy", type = "Limit", quantity = 10, price = 20m,
            requestHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()
        });

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
