using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TradeSimulator.Exchange.Domain.ValueObjects;
using TradeSimulator.Exchange.Features.ImportInstruments;
using TradeSimulator.Exchange.Infrastructure.Database;
using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Exchange.Tests;

[Collection(InfrastructureCollection.Name)]
public sealed class ExchangeIntegrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ModuleFactory<Program> _factory = null!;
    private RabbitMqProbe _rabbit = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ModuleFactory<Program>(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Exchange"] = await infrastructure.CreateDatabaseAsync(),
            ["RabbitMq:Uri"] = infrastructure.RabbitMqUri
        });
        _rabbit = await RabbitMqProbe.ConnectAsync(infrastructure.RabbitMqUri);
        await _rabbit.ListenAsync("trade-simulator.exchange.order.events");
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task Instrument_stored_in_postgres_is_served_by_the_api()
    {
        var store = _factory.Services.GetRequiredService<PostgresInstrumentStore>();
        await store.UpsertAsync(
            [new InstrumentRecord(InstrumentId.New(), Ticker.TryCreate("PETR4").ResultValue, "PETROBRAS PN")],
            CancellationToken.None);

        var instrument = await _client.GetFromJsonAsync<JsonElement>("/api/instruments/PETR4");

        Assert.Equal("PETR4", instrument.GetProperty("ticker").GetString());
        Assert.Equal("PETROBRAS PN", instrument.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/instruments/VALE3")).StatusCode);
    }

    [Fact]
    public async Task Invalid_dispatched_order_received_from_rabbitmq_is_rejected_with_a_published_event()
    {
        var orderId = Guid.CreateVersion7();
        await _rabbit.WaitForQueueAsync("trade-simulator.exchange.order.commands");

        await _rabbit.PublishAsync("trade-simulator-order-commands", "OrderDispatched", JsonSerializer.Serialize(new
        {
            id = Guid.CreateVersion7(),
            eventType = "OrderDispatched",
            @event = new { orderId, ticker = "", side = "Buy", type = "Limit", quantity = 10, price = 20m }
        }));

        var published = await _rabbit.NextAsync(m => m.RoutingKey == "OrderRejected");
        Assert.Contains(orderId.ToString(), published.Body);
    }

    [Fact]
    public async Task Cancel_of_an_unknown_order_is_rejected_with_a_traced_event()
    {
        var orderId = Guid.CreateVersion7();
        await _rabbit.WaitForQueueAsync("trade-simulator.exchange.order.commands");

        await _rabbit.PublishAsync("trade-simulator-order-commands", "OrderCancelRequested", JsonSerializer.Serialize(new
        {
            id = Guid.CreateVersion7(),
            eventType = "OrderCancelRequested",
            @event = new { orderId }
        }));

        var published = await _rabbit.NextAsync(m => m.RoutingKey == "OrderCancelRejected");
        Assert.Contains(orderId.ToString(), published.Body);
        Assert.NotNull(published.Headers);
        Assert.True(published.Headers!.ContainsKey("traceparent"));
    }
}
