using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Quotes.Tests;

[Collection(InfrastructureCollection.Name)]
public sealed class QuotesIntegrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ModuleFactory<Program> _factory = null!;
    private RabbitMqProbe _rabbit = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ModuleFactory<Program>(new Dictionary<string, string?> { ["RabbitMq:Uri"] = infrastructure.RabbitMqUri });
        _rabbit = await RabbitMqProbe.ConnectAsync(infrastructure.RabbitMqUri);
        _client = _factory.CreateClient();
        await _rabbit.WaitForQueueAsync("trade-simulator.quotes.quote.events");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task Quote_received_from_rabbitmq_is_served_by_the_api()
    {
        await _rabbit.PublishAsync("trade-simulator.exchange.quote.events", "quote.PETR4", JsonSerializer.Serialize(new
        {
            id = Guid.CreateVersion7(),
            eventType = "Quote",
            @event = new
            {
                ticker = "PETR4", open = 20m, close = 19.5m, high = 20.5m, low = 19.8m, last = 20.2m,
                variation = "Up", timestamp = DateTimeOffset.UtcNow
            }
        }));

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync("/api/quotes/PETR4/latest");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var quote = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(20.2m, quote.GetProperty("last").GetDecimal());
                Assert.Equal("Up", quote.GetProperty("variation").GetString());
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("Quote was not served within 20s.");
    }

    [Fact]
    public async Task Malformed_quote_is_dropped_and_does_not_break_the_consumer()
    {
        await _rabbit.PublishAsync("trade-simulator.exchange.quote.events", "quote.VALE3", "{not json");
        await _rabbit.PublishAsync("trade-simulator.exchange.quote.events", "quote.VALE3", JsonSerializer.Serialize(new
        {
            id = Guid.CreateVersion7(),
            eventType = "Quote",
            @event = new
            {
                ticker = "VALE3", open = 60m, close = 59m, high = 61m, low = 58m, last = 60.5m,
                variation = "Down", timestamp = DateTimeOffset.UtcNow
            }
        }));

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if ((await _client.GetAsync("/api/quotes/VALE3/latest")).StatusCode == HttpStatusCode.OK)
                return;
            await Task.Delay(100);
        }

        Assert.Fail("Consumer did not recover from the malformed message.");
    }
}
