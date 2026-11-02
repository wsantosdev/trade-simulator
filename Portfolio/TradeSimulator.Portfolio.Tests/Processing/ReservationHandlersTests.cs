using TradeSimulator.Portfolio.Features.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Processors.ReleaseReservation;
using TradeSimulator.Portfolio.Features.ReserveAssets;
using TradeSimulator.Portfolio.Processors.SettleReservation;
using TradeSimulator.Portfolio.Features.DepositAsset;

namespace TradeSimulator.Portfolio.Tests.Processing;

public sealed class ReservationHandlersTests
{
    private readonly FakeStore _store = new();
    private readonly ReserveAssetsHandler _reserve;
    private readonly SettleReservationHandler _settle;
    private readonly ReleaseReservationHandler _release;
    private readonly DepositAssetHandler _commands;
    private readonly Guid _account = Guid.CreateVersion7();

    public ReservationHandlersTests()
    {
        var metrics = new Infrastructure.FakeMetrics();
        _reserve = new(_store, new FakeNotifier(), metrics, TimeProvider.System, NullLogger<ReserveAssetsHandler>.Instance);
        _settle = new(_store, new FakeNotifier(), metrics, TimeProvider.System, NullLogger<SettleReservationHandler>.Instance);
        _release = new(_store, new FakeNotifier(), metrics, TimeProvider.System, NullLogger<ReleaseReservationHandler>.Instance);
        _commands = new(_store, new FakeNotifier(), metrics, TimeProvider.System, NullLogger<DepositAssetHandler>.Instance);
    }

    private Task Deposit(long quantity) =>
        _commands.ExecuteAsync(_account, "PETR4", quantity, default);

    private Task<ReservationResult> Reserve(long quantity, Guid id, string ticker = "PETR4") =>
        _reserve.ExecuteAsync(id, _account, ticker, quantity, default);

    private Task<SettlementResult> Settle(Guid id, long quantity, bool close, Guid? operationId = null) =>
        _settle.ExecuteAsync(id, operationId ?? Guid.CreateVersion7(), quantity, close, default);

    private async Task<Guid> ReserveAsync(long quantity)
    {
        await Deposit(100);
        var id = Guid.CreateVersion7();
        Assert.True((await Reserve(quantity, id)).Reserved);
        return id;
    }

    [Fact]
    public async Task Reservation_blocks_the_quantity()
    {
        await Deposit(100);
        var id = Guid.CreateVersion7();

        var result = await Reserve(40, id);

        Assert.True(result.Reserved);
        var asset = Assert.Single(_store.Assets);
        Assert.Equal(40, asset.Blocked.Value);
        Assert.Equal(60, asset.Available.Value);
    }

    [Fact]
    public async Task Reservation_failures_are_typed()
    {
        await Deposit(10);

        Assert.Equal(ReservationFailure.InsufficientQuantity, (await Reserve(40, Guid.CreateVersion7())).Failure);
        Assert.Equal(ReservationFailure.AssetNotFound, (await Reserve(1, Guid.CreateVersion7(), "VALE3")).Failure);
        Assert.Equal(ReservationFailure.Invalid, (await Reserve(0, Guid.CreateVersion7())).Failure);
        Assert.Equal(ReservationFailure.Invalid, (await Reserve(1, Guid.Empty)).Failure);
        Assert.Equal(10, Assert.Single(_store.Assets).Available.Value);
    }

    [Fact]
    public async Task Repeated_reservation_does_not_block_twice_and_a_different_one_conflicts()
    {
        await Deposit(100);
        var id = Guid.CreateVersion7();

        Assert.True((await Reserve(40, id)).Reserved);
        Assert.True((await Reserve(40, id)).Reserved);
        Assert.Equal(ReservationFailure.Conflict, (await Reserve(30, id)).Failure);

        Assert.Equal(40, Assert.Single(_store.Assets).Blocked.Value);
    }

    [Fact]
    public async Task Partial_settlement_withdraws_the_quantity_and_closing_unblocks_the_leftover()
    {
        var id = await ReserveAsync(40);

        await Settle(id, 10, close: false);
        var asset = Assert.Single(_store.Assets);
        Assert.Equal(30, asset.Blocked.Value);
        Assert.Equal(60, asset.Available.Value);

        await Settle(id, 20, close: true);
        asset = Assert.Single(_store.Assets);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(70, asset.Available.Value);
    }

    [Fact]
    public async Task Settling_the_whole_quantity_closes_the_reservation_without_leftover()
    {
        var id = await ReserveAsync(40);

        await Settle(id, 40, close: true);

        var asset = Assert.Single(_store.Assets);
        Assert.Equal(60, asset.Total.Value);
        Assert.Equal(0, asset.Blocked.Value);
    }

    [Fact]
    public async Task Settled_quantity_is_limited_to_the_remaining_one()
    {
        var id = await ReserveAsync(40);

        await Settle(id, 500, close: false);

        var asset = Assert.Single(_store.Assets);
        Assert.Equal(60, asset.Total.Value);
        Assert.Equal(0, asset.Blocked.Value);
    }

    [Fact]
    public async Task Duplicated_settlement_is_applied_once_and_closed_reservations_ignore_new_ones()
    {
        var id = await ReserveAsync(40);
        var operation = Guid.CreateVersion7();

        await Settle(id, 10, close: false, operation);
        await Settle(id, 10, close: false, operation);
        Assert.Equal(30, Assert.Single(_store.Assets).Blocked.Value);

        await Settle(id, 0, close: true);
        Assert.Equal(SettlementResult.Settled, await Settle(id, 5, close: true));
        var asset = Assert.Single(_store.Assets);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(90, asset.Available.Value);
    }

    [Fact]
    public async Task Unknown_reservation_and_invalid_settlements_are_reported()
    {
        var id = await ReserveAsync(40);

        Assert.Equal(SettlementResult.ReservationNotFound, await Settle(Guid.CreateVersion7(), 1, close: false));
        Assert.Equal(SettlementResult.Invalid, await Settle(id, -1, close: false));
        Assert.Equal(SettlementResult.Invalid, await Settle(id, 1, close: false, Guid.Empty));
    }

    [Fact]
    public async Task Release_unblocks_what_is_left_and_is_idempotent()
    {
        var id = await ReserveAsync(40);
        await Settle(id, 15, close: false);

        await _release.ExecuteAsync(id, default);
        await _release.ExecuteAsync(id, default);
        await _release.ExecuteAsync(Guid.CreateVersion7(), default);

        var asset = Assert.Single(_store.Assets);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(85, asset.Available.Value);
    }
}
