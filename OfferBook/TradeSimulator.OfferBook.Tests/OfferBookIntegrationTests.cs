using System.Net.Http.Json;
using System.Text.Json;
using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.OfferBook.Tests;

[Collection(InfrastructureCollection.Name)]
public sealed class OfferBookIntegrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ModuleFactory<Program> _factory = null!;
    private RabbitMqProbe _rabbit = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ModuleFactory<Program>(new Dictionary<string, string?> { ["RabbitMq:Uri"] = infrastructure.RabbitMqUri });
        _rabbit = await RabbitMqProbe.ConnectAsync(infrastructure.RabbitMqUri);
        await _rabbit.ListenAsync("trade-simulator.offerbook.interest.events");
        _client = _factory.CreateClient();
        await _rabbit.WaitForQueueAsync("trade-simulator.offerbook.book.events");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task Snapshot_received_from_rabbitmq_is_served_by_the_api()
    {
        await _rabbit.PublishAsync("trade-simulator.exchange.offerbook.events", "offerbook.PETR4", Envelope("OfferBookSnapshot", new
        {
            ticker = "PETR4",
            sequence = 1,
            phase = "Open",
            bids = new[] { new { price = 19.9m, quantity = 100, orders = 2 } },
            asks = new[] { new { price = 20.1m, quantity = 50, orders = 1 } },
            timestamp = DateTimeOffset.UtcNow
        }));

        var book = await Eventually(async () =>
        {
            var response = await _client.GetAsync("/api/offerbook/PETR4");
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonElement>() : (JsonElement?)null;
        });

        Assert.Equal(19.9m, book.GetProperty("bids")[0].GetProperty("price").GetDecimal());
        Assert.Equal(20.1m, book.GetProperty("asks")[0].GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Delta_without_a_base_snapshot_requests_a_snapshot_through_rabbitmq()
    {
        await _rabbit.PublishAsync("trade-simulator.exchange.offerbook.events", "offerbook.VALE3", Envelope("OfferBookDelta", new
        {
            ticker = "VALE3",
            sequence = 2,
            previousSequence = 1,
            phase = "Open",
            changes = new[] { new { side = "Bid", price = 60m, quantity = 10, orders = 1 } },
            timestamp = DateTimeOffset.UtcNow
        }));

        var request = await _rabbit.NextAsync(m => m.Body.Contains("OfferBookSnapshotRequested"));
        Assert.Contains("VALE3", request.Body);
    }

    private static string Envelope(string eventType, object payload) =>
        JsonSerializer.Serialize(new { id = Guid.CreateVersion7(), eventType, @event = payload });

    private static async Task<T> Eventually<T>(Func<Task<T?>> read) where T : struct
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await read() is { } value)
                return value;
            await Task.Delay(100);
        }

        throw new TimeoutException("Condition not met within 20s.");
    }
}
