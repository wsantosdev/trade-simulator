using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TradeSimulator.Account.Infrastructure.Messaging;
using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Account.Tests;

[Collection(InfrastructureCollection.Name)]
public sealed class AccountIntegrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ModuleFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private RabbitMqProbe _rabbit = null!;

    public async Task InitializeAsync()
    {
        _factory = new ModuleFactory<Program>(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Accounts"] = await infrastructure.CreateDatabaseAsync(),
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
    public async Task Deposit_is_persisted_and_updates_the_balance()
    {
        var accountId = await OpenAccountAsync();

        var deposit = await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 250.75m });
        Assert.Equal(HttpStatusCode.OK, deposit.StatusCode);

        var account = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}");
        Assert.Equal(250.75m, account.GetProperty("availableBalance").GetDecimal());
    }

    [Fact]
    public async Task Reservation_reserves_funds_synchronously()
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
    public async Task Reservation_without_enough_balance_is_rejected_with_422()
    {
        var accountId = await OpenAccountAsync();

        var response = await ReserveAsync(accountId, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
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

    [Theory]
    [InlineData("OrderCancelled")]
    [InlineData("OrderRejected")]
    [InlineData("OrderScheduleCancelled")]
    public async Task Order_closing_events_return_the_funds(string eventType)
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 1_000m });
        var orderId = Guid.CreateVersion7();
        await ReserveAsync(accountId, orderId);

        await _rabbit.PublishAsync(OrderEventMessages.Exchange, eventType, OrderEventMessages.Cancel(eventType, orderId));

        await AssertBalanceAsync(accountId, available: 1_000m, provisioned: 0m);
    }

    [Fact]
    public async Task Partial_fill_debits_the_executed_value_and_keeps_the_rest_reserved()
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 1_000m });
        var orderId = Guid.CreateVersion7();
        await ReserveAsync(accountId, orderId);
        var eventId = Guid.CreateVersion7();

        // The same event delivered twice is debited once.
        for (var i = 0; i < 2; i++)
            await _rabbit.PublishAsync(OrderEventMessages.Exchange, "OrderPartiallyFilled",
                OrderEventMessages.Fill("OrderPartiallyFilled", orderId, 4, 10m, eventId));

        await AssertBalanceAsync(accountId, available: 800m, provisioned: 160m);
    }

    [Fact]
    public async Task Final_fill_at_a_better_price_releases_the_leftover()
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 1_000m });
        var orderId = Guid.CreateVersion7();
        await ReserveAsync(accountId, orderId);

        await _rabbit.PublishAsync(OrderEventMessages.Exchange, "OrderFilled",
            OrderEventMessages.Fill("OrderFilled", orderId, 10, 15m));

        await AssertBalanceAsync(accountId, available: 850m, provisioned: 0m);
    }

    [Fact]
    public async Task Events_of_unknown_reservations_are_ignored()
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 1_000m });
        var orderId = Guid.CreateVersion7();
        await ReserveAsync(accountId, orderId);

        await _rabbit.PublishAsync(OrderEventMessages.Exchange, "OrderFilled",
            OrderEventMessages.Fill("OrderFilled", Guid.CreateVersion7(), 10, 15m));
        await _rabbit.PublishAsync(OrderEventMessages.Exchange, "OrderCancelled", OrderEventMessages.Cancel("OrderCancelled", orderId));

        // The unknown fill did not block the queue: the later cancellation was applied.
        await AssertBalanceAsync(accountId, available: 1_000m, provisioned: 0m);
    }

    [Fact]
    public async Task Repeated_reservation_with_the_same_id_does_not_reserve_twice()
    {
        var accountId = await OpenAccountAsync();
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount = 1_000m });
        var reservationId = Guid.CreateVersion7();

        await ReserveAsync(accountId, reservationId);
        await ReserveAsync(accountId, reservationId);

        var account = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}");
        Assert.Equal(800m, account.GetProperty("availableBalance").GetDecimal());
    }

    private Task<HttpResponseMessage> ReserveAsync(Guid accountId, Guid reservationId) =>
        _client.PostAsJsonAsync($"/api/accounts/{accountId}/reservations", new { reservationId, amount = 200m });

    // Events are consumed asynchronously: polls until the balances match.
    private async Task AssertBalanceAsync(Guid accountId, decimal available, decimal provisioned)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        JsonElement account;
        do
        {
            account = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}");
            if (account.GetProperty("availableBalance").GetDecimal() == available
                && account.GetProperty("provisionedBalance").GetDecimal() == provisioned)
                return;
            await Task.Delay(100);
        } while (DateTime.UtcNow < deadline);

        Assert.Equal(available, account.GetProperty("availableBalance").GetDecimal());
        Assert.Equal(provisioned, account.GetProperty("provisionedBalance").GetDecimal());
    }

    private async Task<Guid> OpenAccountAsync()
    {
        var opened = await _client.PostAsync("/api/accounts", null);
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        var body = await opened.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accountId").GetGuid();
    }
}
