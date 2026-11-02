using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.Entities;
using TradeSimulator.Portfolio.Domain.Events;
using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Tests;

public sealed class AssetTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Quantity Qty(long value) => Quantity.TryCreate(value).ResultValue;

    private static Asset Open(long quantity = 100) =>
        Asset.Open(
            AssetId.New(),
            AccountId.TryCreate(Guid.CreateVersion7()).ResultValue,
            Ticker.TryCreate("petr4").ResultValue,
            Qty(quantity),
            Now).ResultValue;

    [Fact]
    public void Opening_deposit_creates_asset_with_available_quantity()
    {
        var asset = Open(100);

        Assert.Equal(100, asset.Available.Value);
        Assert.True(asset.Blocked.IsZero);
        Assert.Equal(100, asset.Total.Value);
        Assert.Equal("PETR4", asset.Ticker.Value);
        Assert.IsType<AssetDeposited>(Assert.Single(asset.UncommittedEvents));
    }

    [Fact]
    public void Deposit_increments_available_quantity()
    {
        var asset = Open(100);

        Assert.False(asset.Deposit(Qty(50), Now).IsError);

        Assert.Equal(150, asset.Available.Value);
        Assert.Equal(2, asset.Version);
    }

    [Fact]
    public void Reserve_moves_quantity_from_available_to_blocked()
    {
        var asset = Open(100);

        Assert.False(asset.Reserve(Guid.CreateVersion7(), Qty(40), Now).IsError);

        Assert.Equal(60, asset.Available.Value);
        Assert.Equal(40, asset.Blocked.Value);
        Assert.Equal(100, asset.Total.Value);
        Assert.IsType<AssetBlocked>(asset.UncommittedEvents[^1]);
    }

    [Fact]
    public void Reserve_above_available_returns_typed_error()
    {
        var asset = Open(100);

        Assert.IsType<InsufficientAvailableQuantity>(asset.Reserve(Guid.CreateVersion7(), Qty(101), Now).ErrorValue);
        Assert.Equal(100, asset.Available.Value);
    }

    [Fact]
    public void Repeated_reservation_blocks_once_and_a_different_quantity_conflicts()
    {
        var asset = Open(100);
        var id = Guid.CreateVersion7();

        asset.Reserve(id, Qty(40), Now);
        Assert.False(asset.Reserve(id, Qty(40), Now).IsError);
        Assert.IsType<ReservationConflict>(asset.Reserve(id, Qty(30), Now).ErrorValue);

        Assert.Equal(40, asset.Blocked.Value);
    }

    [Fact]
    public void Partial_settlements_withdraw_and_closing_unblocks_the_leftover()
    {
        var asset = Open(100);
        var id = Guid.CreateVersion7();
        asset.Reserve(id, Qty(40), Now);

        asset.Settle(id, Guid.CreateVersion7(), 15, close: false, Now);
        Assert.Equal(25, asset.Blocked.Value);

        asset.Settle(id, Guid.CreateVersion7(), 10, close: true, Now);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(75, asset.Total.Value);
        Assert.Equal(75, asset.Available.Value);
    }

    [Fact]
    public void Settlement_is_limited_to_the_remaining_quantity_and_closes_it()
    {
        var asset = Open(100);
        var id = Guid.CreateVersion7();
        asset.Reserve(id, Qty(40), Now);

        asset.Settle(id, Guid.CreateVersion7(), 500, close: false, Now);

        Assert.Equal(60, asset.Total.Value);
        Assert.Equal(0, asset.Blocked.Value);
    }

    [Fact]
    public void Repeated_operations_and_closed_reservations_change_nothing()
    {
        var asset = Open(100);
        var id = Guid.CreateVersion7();
        var operation = Guid.CreateVersion7();
        asset.Reserve(id, Qty(40), Now);

        asset.Settle(id, operation, 10, close: false, Now);
        asset.Settle(id, operation, 10, close: false, Now);
        Assert.Equal(30, asset.Blocked.Value);

        asset.CloseReservation(id, Now);
        asset.CloseReservation(id, Now);
        asset.Settle(id, Guid.CreateVersion7(), 5, close: true, Now);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(90, asset.Total.Value);
    }

    [Fact]
    public void Unknown_reservation_is_reported()
    {
        var asset = Open(100);

        Assert.IsType<ReservationNotFound>(asset.Settle(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, false, Now).ErrorValue);
        Assert.IsType<ReservationNotFound>(asset.CloseReservation(Guid.CreateVersion7(), Now).ErrorValue);
    }

    [Fact]
    public void Emptied_asset_can_be_deposited_again()
    {
        var asset = Open(100);
        var id = Guid.CreateVersion7();
        asset.Reserve(id, Qty(100), Now);
        asset.Settle(id, Guid.CreateVersion7(), 100, close: true, Now);

        Assert.True(asset.IsEmpty);
        Assert.False(asset.Deposit(Qty(5), Now).IsError);
        Assert.Equal(5, asset.Available.Value);
    }

    [Fact]
    public void Quantity_above_one_million_is_rejected_by_every_operation()
    {
        var asset = Open(Asset.MaxOperationQuantity);
        var tooMuch = Qty(Asset.MaxOperationQuantity + 1);

        Assert.IsType<QuantityAboveLimit>(asset.Deposit(tooMuch, Now).ErrorValue);
        Assert.IsType<QuantityAboveLimit>(asset.Reserve(Guid.CreateVersion7(), tooMuch, Now).ErrorValue);
        Assert.IsType<QuantityAboveLimit>(Asset.Open(AssetId.New(), asset.AccountId, asset.Ticker, tooMuch, Now).ErrorValue);
    }

    [Fact]
    public void Default_quantity_is_rejected_by_operations()
    {
        var asset = Open(100);

        Assert.IsType<InvalidQuantity>(asset.Deposit(default, Now).ErrorValue);
        Assert.IsType<InvalidQuantity>(asset.Reserve(Guid.CreateVersion7(), default, Now).ErrorValue);
    }

    [Fact]
    public void Total_is_not_limited_by_the_operation_ceiling()
    {
        var asset = Open(Asset.MaxOperationQuantity);

        Assert.False(asset.Deposit(Qty(Asset.MaxOperationQuantity), Now).IsError);
        Assert.Equal(2 * Asset.MaxOperationQuantity, asset.Total.Value);
    }

    [Fact]
    public void Rehydrate_replays_history_to_the_same_state()
    {
        var asset = Open(100);
        var id = Guid.CreateVersion7();
        var operation = Guid.CreateVersion7();
        asset.Deposit(Qty(20), Now);
        asset.Reserve(id, Qty(50), Now);
        asset.Settle(id, operation, 10, close: false, Now);

        var restored = Asset.Rehydrate(asset.UncommittedEvents.Cast<AssetEvent>().ToList()).ResultValue;

        Assert.Equal(asset.Id, restored.Id);
        Assert.Equal(asset.Available, restored.Available);
        Assert.Equal(asset.Blocked, restored.Blocked);
        Assert.Equal(asset.Version, restored.Version);
        Assert.Empty(restored.UncommittedEvents);
        Assert.Equal(40, restored.Blocked.Value);
        Assert.False(restored.Settle(id, operation, 10, close: false, Now).IsError);
        Assert.Empty(restored.UncommittedEvents);
    }

    [Fact]
    public void Asset_id_is_derived_from_account_and_ticker()
    {
        var account = AccountId.TryCreate(Guid.CreateVersion7()).ResultValue;
        var petr4 = Ticker.TryCreate("PETR4").ResultValue;

        Assert.Equal(AssetId.For(account, petr4), AssetId.For(account, petr4));
        Assert.NotEqual(AssetId.For(account, petr4), AssetId.For(account, Ticker.TryCreate("VALE3").ResultValue));
        Assert.False(AssetId.TryCreate(AssetId.For(account, petr4).Guid).IsError);
    }

    [Fact]
    public void CommitEvents_clears_uncommitted_events()
    {
        var asset = Open(100);

        asset.CommitEvents();

        Assert.Empty(asset.UncommittedEvents);
    }
}
