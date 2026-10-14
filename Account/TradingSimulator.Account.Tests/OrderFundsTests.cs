global using FundsReservationResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Account.Application.FundsReservedMessage, TradingSimulator.Account.Application.FundsReservationError>>;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moonad;
using TradingSimulator.Account.Application;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Infrastructure;
using Xunit;

namespace TradingSimulator.Account.Tests;

public sealed class OrderFundsTests
{
    private static readonly JsonSerializerOptions Options = AccountJson.Options;

    [Fact]
    public void Release_moves_provisioned_back_to_available_and_debit_removes_provisioned()
    {
        var account = Domain.Account.Open(AccountId.New()).ResultValue;
        account.Deposit(100m);
        account.Reserve(60m, OrderId.New());

        Assert.False(account.Release(10m).IsError);
        Assert.Equal(50m, account.ProvisionedBalance);
        Assert.Equal(50m, account.AvailableBalance);

        Assert.False(account.Debit(20m).IsError);
        Assert.Equal(30m, account.ProvisionedBalance);
        Assert.Equal(50m, account.AvailableBalance);

        Assert.IsType<DebitExceedsProvisionedBalance>(account.Debit(31m).ErrorValue);
        Assert.IsType<ReleaseExceedsProvisionedBalance>(account.Release(31m).ErrorValue);
    }

    [Fact]
    public void Market_amount_has_five_percent_margin_rounded_up_to_the_cent()
    {
        Assert.Equal(10.52m, OrderFundsCalculator.ForMarket(1, 10.01m).ResultValue);
        Assert.Equal(1050m, OrderFundsCalculator.ForMarket(10, 100m).ResultValue);
        Assert.Equal(100m, OrderFundsCalculator.ForLimit(10, 10m).ResultValue);
        Assert.True(OrderFundsCalculator.ForLimit(0, 10m).IsError);
    }

    [Fact]
    public async Task Sell_orders_are_discarded_without_response()
    {
        var store = new FakeStore();
        await Processor(store).ProcessOrderSubmittedAsync(OrderSubmitted("Sell", "Limit", 10, 5m), Options, default);

        Assert.Equal(1, store.Rejects);
        Assert.Null(store.LastFailure);
        Assert.Equal(0, store.Reserves);
    }

    [Fact]
    public async Task Limit_buy_reserves_quantity_times_price()
    {
        var store = new FakeStore();
        await Processor(store).ProcessOrderSubmittedAsync(OrderSubmitted("Buy", "Limit", 10, 5.5m), Options, default);

        Assert.Equal(55m, store.ReservedAmount);
    }

    [Fact]
    public async Task Market_buy_uses_best_ask_with_margin()
    {
        var store = new FakeStore();
        await Processor(store, 20m).ProcessOrderSubmittedAsync(OrderSubmitted("Buy", "Market", 2, null), Options, default);

        Assert.Equal(42m, store.ReservedAmount);
    }

    [Fact]
    public async Task Market_buy_without_price_fails_with_price_unavailable()
    {
        var store = new FakeStore();
        await Processor(store, null).ProcessOrderSubmittedAsync(OrderSubmitted("Buy", "Market", 2, null), Options, default);

        var error = store.LastFailure!.Event.ErrorValue;
        Assert.Equal(FundsReservationErrorKind.PriceUnavailable, error.Kind);
        Assert.Equal(0, store.Reserves);
    }

    [Fact]
    public async Task Insufficient_funds_produces_failure_response()
    {
        var store = new FakeStore { ReserveError = new ReservationExceedsAvailableBalance() };
        await Processor(store).ProcessOrderSubmittedAsync(OrderSubmitted("Buy", "Limit", 10, 5m), Options, default);

        Assert.Equal(FundsReservationErrorKind.InsufficientFunds, store.LastFailure!.Event.ErrorValue.Kind);
    }

    [Fact]
    public async Task Fill_debits_and_cancel_releases()
    {
        var store = new FakeStore();
        var orderId = Guid.CreateVersion7();
        var processor = Processor(store);

        await processor.ProcessExchangeEventAsync(Event("OrderPartiallyFilled", orderId, ",\"executedQuantity\":3,\"price\":2.5"), Options, default);
        await processor.ProcessExchangeEventAsync(Event("OrderCancelled", orderId, ""), Options, default);

        Assert.Equal(7.5m, store.DebitedAmount);
        Assert.False(store.DebitFinal);
        Assert.Equal(1, store.Releases);
    }

    [Fact]
    public async Task Fill_without_reservation_is_retried_then_discarded()
    {
        var store = new FakeStore { Outcome = OrderFundsOutcome.UnknownOrder };
        var processor = Processor(store);
        var body = Event("OrderFilled", Guid.CreateVersion7(), ",\"executedQuantity\":1,\"executionPrice\":2");

        for (var i = 1; i < OrderFundsProcessor.MaxFillAttempts; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessExchangeEventAsync(body, Options, default));
        await processor.ProcessExchangeEventAsync(body, Options, default);

        Assert.True(store.DebitFinal);
    }

    [Fact]
    public void Response_result_round_trips_as_json()
    {
        var order = Guid.CreateVersion7();
        var response = new FundsReservationResponse(Guid.CreateVersion7(), "FundsReservationResult",
            Result<FundsReservedMessage, FundsReservationError>.Ok(new FundsReservedMessage(order, order, 10m)));

        var json = JsonSerializer.Serialize(response, Options);
        var back = JsonSerializer.Deserialize<FundsReservationResponse>(json, Options)!;

        Assert.Contains("\"isError\":false", json);
        Assert.Equal(10m, back.Event.ResultValue.Amount);
    }

    private static OrderFundsProcessor Processor(FakeStore store, decimal? ask = 1m) =>
        new(store, new FakeOfferBook(ask), NullLogger<OrderFundsProcessor>.Instance);

    private static string OrderSubmitted(string side, string type, long quantity, decimal? price) =>
        $$$"""
        {"id":"{{{Guid.CreateVersion7()}}}","eventType":"OrderSubmitted","event":{"orderId":{"guid":"{{{Guid.CreateVersion7()}}}"},"accountId":{"guid":"{{{Guid.CreateVersion7()}}}"},"ticker":"PETR4","side":"{{{side}}}","type":"{{{type}}}","quantity":{{{quantity}}},"price":{{{(price is null ? "null" : price.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}}}}}
        """;

    private static string Event(string type, Guid orderId, string extra) =>
        $$$"""{"id":"{{{Guid.CreateVersion7()}}}","eventType":"{{{type}}}","event":{"orderId":"{{{orderId}}}"{{{extra}}}}}""";

    private sealed class FakeOfferBook(decimal? ask) : IOfferBookClient
    {
        public Task<Result<decimal, IAccountError>> GetBestAskAsync(string ticker, CancellationToken cancellationToken) =>
            Task.FromResult(ask is null
                ? Result<decimal, IAccountError>.Error(new PriceUnavailable())
                : Result<decimal, IAccountError>.Ok(ask.Value));
    }

    private sealed class FakeStore : IOrderFundsStore
    {
        public int Reserves, Rejects, Releases;
        public decimal ReservedAmount, DebitedAmount;
        public bool DebitFinal;
        public IAccountError? ReserveError;
        public OrderFundsOutcome Outcome = OrderFundsOutcome.Applied;
        public FundsReservationResponse? LastFailure;

        public Task<Result<OrderFundsOutcome, IAccountError>> ReserveAsync(Guid envelopeId, OrderId orderId, AccountId accountId, decimal amount, FundsReservationResponse success, CancellationToken cancellationToken)
        {
            if (ReserveError is not null)
                return Task.FromResult(Result<OrderFundsOutcome, IAccountError>.Error(ReserveError));
            Reserves++;
            ReservedAmount = amount;
            return Task.FromResult<Result<OrderFundsOutcome, IAccountError>>(OrderFundsOutcome.Applied);
        }

        public Task<OrderFundsOutcome> RejectAsync(Guid envelopeId, OrderId orderId, string reason, FundsReservationResponse? failure, CancellationToken cancellationToken)
        {
            Rejects++;
            LastFailure = failure;
            return Task.FromResult(OrderFundsOutcome.Applied);
        }

        public Task<Result<OrderFundsOutcome, IAccountError>> DebitAsync(Guid envelopeId, OrderId orderId, decimal amount, bool final, CancellationToken cancellationToken)
        {
            DebitedAmount = amount;
            DebitFinal = final;
            return Task.FromResult<Result<OrderFundsOutcome, IAccountError>>(Outcome);
        }

        public Task<Result<OrderFundsOutcome, IAccountError>> ReleaseAsync(Guid envelopeId, OrderId orderId, CancellationToken cancellationToken)
        {
            Releases++;
            return Task.FromResult<Result<OrderFundsOutcome, IAccountError>>(Outcome);
        }
    }
}
