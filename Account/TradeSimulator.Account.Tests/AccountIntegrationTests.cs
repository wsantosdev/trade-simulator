using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Account.Tests;

[Collection(InfrastructureCollection.Name)]
public sealed class AccountIntegrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ModuleFactory<Program> _factory = null!;
    private RabbitMqProbe _rabbit = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ModuleFactory<Program>(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Accounts"] = await infrastructure.CreateDatabaseAsync(),
            ["ConnectionStrings:Redis"] = infrastructure.RedisConnection,
            ["RabbitMq:Uri"] = infrastructure.RabbitMqUri
        });
        _rabbit = await RabbitMqProbe.ConnectAsync(infrastructure.RabbitMqUri);
        await _rabbit.ListenAsync("trade-simulator.account.reservations");
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task Deposit_is_persisted_and_updates_the_balance()
    {
        var accountId = await OpenAccountAsync();

        var deposit = await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 250.75m });
        Assert.Equal(HttpStatusCode.OK, deposit.StatusCode);

        var account = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}");
        Assert.Equal(250.75m, account.GetProperty("availableBalance").GetDecimal());
    }

    [Fact]
    public async Task Inserting_an_event_shifts_later_versions_and_rebuilds_the_balance()
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 100m });
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/withdrawals", new { amount = 60m });

        var inserted = await _client.PostAsJsonAsync($"/api/accounts/{accountId}/stream/events",
            new { position = 3, type = "deposit", amount = 25m });
        Assert.Equal(HttpStatusCode.OK, inserted.StatusCode);

        var view = await inserted.Content.ReadFromJsonAsync<JsonElement>();
        var versions = view.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToArray();
        Assert.Equal(["AccountOpened", "MoneyDeposited", "MoneyDeposited", "MoneyWithdrawn"], versions);
        Assert.Equal(65m, view.GetProperty("availableBalance").GetDecimal());
        Assert.True(view.GetProperty("inSync").GetBoolean());
        var account = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}");
        Assert.Equal(65m, account.GetProperty("availableBalance").GetDecimal());
    }

    [Fact]
    public async Task Inserting_an_event_that_invalidates_the_replay_is_rejected_and_changes_nothing()
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 100m });
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/withdrawals", new { amount = 60m });

        var rejected = await _client.PostAsJsonAsync($"/api/accounts/{accountId}/stream/events",
            new { position = 2, type = "withdrawal", amount = 10m });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        var view = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/stream");
        Assert.Equal(3, view.GetProperty("events").GetArrayLength());
        Assert.Equal(40m, view.GetProperty("availableBalance").GetDecimal());
    }

    [Fact]
    public async Task Order_reservation_reserves_funds_synchronously()
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 1_000m });

        var response = await ReserveAsync(accountId, Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var account = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}");
        Assert.Equal(200m, account.GetProperty("provisionedBalance").GetDecimal());
        Assert.Equal(800m, account.GetProperty("availableBalance").GetDecimal());
    }

    [Fact]
    public async Task Order_reservation_without_enough_balance_is_rejected_with_422()
    {
        var accountId = await OpenAccountAsync();

        var response = await ReserveAsync(accountId, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InsufficientFunds", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Concurrent_order_reservations_never_exceed_the_balance()
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 1_000m });

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ReserveAsync(accountId, Guid.CreateVersion7())));

        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var account = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}");
        Assert.Equal(0m, account.GetProperty("availableBalance").GetDecimal());
    }

    [Fact]
    public async Task Released_reservation_returns_the_funds()
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 1_000m });
        var orderId = Guid.CreateVersion7();
        await ReserveAsync(accountId, orderId);

        var released = await _client.DeleteAsync($"/api/order-reservations/{orderId}");
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);

        var account = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}");
        Assert.Equal(1_000m, account.GetProperty("availableBalance").GetDecimal());
    }

    private Task<HttpResponseMessage> ReserveAsync(Guid accountId, Guid orderId) =>
        _client.PostAsJsonAsync("/api/order-reservations", new
        {
            orderId,
            accountId,
            ticker = "PETR4",
            side = "Buy",
            type = "Limit",
            quantity = 10,
            price = 20m
        });

    private async Task<Guid> OpenAccountAsync()
    {
        var opened = await _client.PostAsync("/api/accounts", null);
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        var body = await opened.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accountId").GetGuid();
    }
}
