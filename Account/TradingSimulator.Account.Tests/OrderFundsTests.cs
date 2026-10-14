global using FundsReservationResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Account.Infrastructure.Messaging.FundsReservedMessage, TradingSimulator.Account.Infrastructure.Messaging.FundsReservationError>>;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moonad;
using Xunit;
using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.Enums;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Infrastructure.Messaging;
using TradingSimulator.Account.Infrastructure.Serialization;
using TradingSimulator.Account.Processors.OrderFunds;

namespace TradingSimulator.Account.Tests;

public sealed class OrderFundsTests
{
    private static readonly JsonSerializerOptions Options = AccountJson.Options;

    [Fact]
    public void Release_moves_provisioned_back_to_available_and_debit_removes_provisioned()
    {
        var account = Domain.Entities.Account.Open(AccountId.New()).ResultValue;
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
        await Processor(store).ReserveAsync(Guid.CreateVersion7(), Submission(OrderSide.Sell, OrderType.Limit, 10, 5m), default);

        Assert.Equal(1, store.Rejects);
        Assert.Null(store.LastFailure);
        Assert.Equal(0, store.Reserves);
    }

    [Fact]
    public async Task Limit_buy_reserves_quantity_times_price()
    {
        var store = new FakeStore();
        await Processor(store).ReserveAsync(Guid.CreateVersion7(), Submission(OrderSide.Buy, OrderType.Limit, 10, 5.5m), default);

        Assert.Equal(55m, store.ReservedAmount);
    }

    [Fact]
    public async Task Market_buy_uses_best_ask_with_margin()
    {
        var store = new FakeStore();
        await Processor(store, 20m).ReserveAsync(Guid.CreateVersion7(), Submission(OrderSide.Buy, OrderType.Market, 2, null), default);

        Assert.Equal(42m, store.ReservedAmount);
    }

    [Fact]
    public async Task Market_buy_without_price_fails_with_price_unavailable()
    {
        var store = new FakeStore();
        await Processor(store, null).ReserveAsync(Guid.CreateVersion7(), Submission(OrderSide.Buy, OrderType.Market, 2, null), default);

        Assert.Equal(FundsReservationErrorKind.PriceUnavailable, store.LastFailure!.Kind);
        Assert.Equal(0, store.Reserves);
    }

    [Fact]
    public async Task Insufficient_funds_produces_failure_response()
    {
        var store = new FakeStore { ReserveError = new ReservationExceedsAvailableBalance() };
        await Processor(store).ReserveAsync(Guid.CreateVersion7(), Submission(OrderSide.Buy, OrderType.Limit, 10, 5m), default);

        Assert.Equal(FundsReservationErrorKind.InsufficientFunds, store.LastFailure!.Kind);
    }

    [Fact]
    public async Task Fill_debits_and_cancel_releases()
    {
        var store = new FakeStore();
        var orderId = OrderId.New();
        var processor = Processor(store);

        await processor.SettleFillAsync(Guid.CreateVersion7(), orderId, 3, 2.5m, false, default);
        await processor.SettleClosureAsync(Guid.CreateVersion7(), orderId, default);

        Assert.Equal(7.5m, store.DebitedAmount);
        Assert.False(store.DebitFinal);
        Assert.Equal(1, store.Releases);
    }

    [Fact]
    public async Task Fill_without_reservation_is_retried_then_discarded()
    {
        var store = new FakeStore { Outcome = OrderFundsOutcome.UnknownOrder };
        var metrics = new FakeMetrics();
        var processor = Processor(store, metrics: metrics);
        var envelopeId = Guid.CreateVersion7();
        var orderId = OrderId.New();

        for (var i = 1; i < OrderFundsProcessor.MaxFillAttempts; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.SettleFillAsync(envelopeId, orderId, 1, 2m, true, default));
        await processor.SettleFillAsync(envelopeId, orderId, 1, 2m, true, default);

        Assert.True(store.DebitFinal);
        Assert.Equal(1, metrics.Abandoned);
        Assert.Equal(OrderFundsProcessor.MaxFillAttempts - 1, metrics.Settlements.Count(s => s == "fill:retry"));
    }

    [Fact]
    public async Task Reservation_result_is_recorded_without_identifiers()
    {
        var metrics = new FakeMetrics();
        await Processor(new FakeStore(), metrics: metrics)
            .ReserveAsync(Guid.CreateVersion7(), Submission(OrderSide.Sell, OrderType.Limit, 10, 5m), default);
        await Processor(new FakeStore(), metrics: metrics)
            .ReserveAsync(Guid.CreateVersion7(), Submission(OrderSide.Buy, OrderType.Limit, 10, 5m), default);

        Assert.Equal(["ignored_not_buy", "reserved"], metrics.Reservations);
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

    private static OrderFundsProcessor Processor(FakeStore store, decimal? ask = 1m, FakeMetrics? metrics = null) =>
        new(store, new FakeOfferBook(ask), metrics ?? new FakeMetrics(), NullLogger<OrderFundsProcessor>.Instance);

    private sealed class FakeMetrics : IAccountMetrics
    {
        public List<string> Reservations { get; } = [];
        public List<string> Settlements { get; } = [];
        public int Abandoned { get; private set; }

        public void OperationCompleted(string operation, decimal? amount, IAccountError? error) { }
        public void ReservationCompleted(string result, TimeSpan duration) => Reservations.Add(result);
        public void SettlementCompleted(string type, string result) => Settlements.Add($"{type}:{result}");
        public void SettlementAbandoned(string type) => Abandoned++;
    }

    private static OrderSubmission Submission(OrderSide side, OrderType type, long quantity, decimal? price) =>
        new(OrderId.New(), Guid.CreateVersion7(), "PETR4", side, type, quantity, price);

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
        public FundsReservationFailure? LastFailure;

        public Task<Result<OrderFundsOutcome, IAccountError>> ReserveAsync(Guid envelopeId, OrderId orderId, AccountId accountId, decimal amount, CancellationToken cancellationToken)
        {
            if (ReserveError is not null)
                return Task.FromResult(Result<OrderFundsOutcome, IAccountError>.Error(ReserveError));
            Reserves++;
            ReservedAmount = amount;
            return Task.FromResult<Result<OrderFundsOutcome, IAccountError>>(OrderFundsOutcome.Applied);
        }

        public Task<OrderFundsOutcome> RejectAsync(Guid envelopeId, OrderId orderId, string reason, FundsReservationFailure? failure, CancellationToken cancellationToken)
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
