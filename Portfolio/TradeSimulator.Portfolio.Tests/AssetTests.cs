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
    public void Block_moves_quantity_from_available_to_blocked()
    {
        var asset = Open(100);

        Assert.False(asset.Block(Qty(40), Now).IsError);

        Assert.Equal(60, asset.Available.Value);
        Assert.Equal(40, asset.Blocked.Value);
        Assert.Equal(100, asset.Total.Value);
        Assert.IsType<AssetBlocked>(asset.UncommittedEvents[^1]);
    }

    [Fact]
    public void Block_may_use_the_whole_available_quantity()
    {
        var asset = Open(100);

        Assert.False(asset.Block(Qty(100), Now).IsError);
        Assert.True(asset.Available.IsZero);
    }

    [Fact]
    public void Block_above_available_returns_typed_error_without_event()
    {
        var asset = Open(100);

        var result = asset.Block(Qty(101), Now);

        Assert.IsType<InsufficientAvailableQuantity>(result.ErrorValue);
        Assert.Single(asset.UncommittedEvents);
        Assert.Equal(100, asset.Available.Value);
    }

    [Fact]
    public void Unblock_returns_quantity_to_available()
    {
        var asset = Open(100);
        asset.Block(Qty(40), Now);

        Assert.False(asset.Unblock(Qty(30), Now).IsError);

        Assert.Equal(90, asset.Available.Value);
        Assert.Equal(10, asset.Blocked.Value);
        Assert.IsType<AssetUnblocked>(asset.UncommittedEvents[^1]);
    }

    [Fact]
    public void Unblock_above_blocked_returns_typed_error()
    {
        var asset = Open(100);
        asset.Block(Qty(40), Now);

        Assert.IsType<InsufficientBlockedQuantity>(asset.Unblock(Qty(41), Now).ErrorValue);
        Assert.Equal(40, asset.Blocked.Value);
    }

    [Fact]
    public void Partial_withdrawals_consume_only_blocked_quantity()
    {
        var asset = Open(100);
        asset.Block(Qty(40), Now);

        Assert.False(asset.Withdraw(Qty(15), Now).IsError);
        Assert.False(asset.Withdraw(Qty(10), Now).IsError);

        Assert.Equal(15, asset.Blocked.Value);
        Assert.Equal(60, asset.Available.Value);
        Assert.Equal(75, asset.Total.Value);
        Assert.False(asset.IsRemoved);
        Assert.IsType<AssetWithdrawn>(asset.UncommittedEvents[^1]);
    }

    [Fact]
    public void Withdraw_above_blocked_returns_typed_error()
    {
        var asset = Open(100);
        asset.Block(Qty(40), Now);

        Assert.IsType<InsufficientBlockedQuantity>(asset.Withdraw(Qty(41), Now).ErrorValue);
        Assert.Equal(40, asset.Blocked.Value);
    }

    [Fact]
    public void Withdraw_that_zeroes_available_and_blocked_removes_asset()
    {
        var asset = Open(100);
        asset.Block(Qty(100), Now);

        Assert.False(asset.Withdraw(Qty(100), Now).IsError);

        Assert.True(asset.Total.IsZero);
        Assert.True(asset.IsRemoved);
    }

    [Fact]
    public void Removed_asset_rejects_every_operation()
    {
        var asset = Open(100);
        asset.Block(Qty(100), Now);
        asset.Withdraw(Qty(100), Now);

        Assert.IsType<AssetNotFound>(asset.Deposit(Qty(1), Now).ErrorValue);
        Assert.IsType<AssetNotFound>(asset.Block(Qty(1), Now).ErrorValue);
        Assert.IsType<AssetNotFound>(asset.Withdraw(Qty(1), Now).ErrorValue);
        Assert.IsType<AssetNotFound>(asset.Unblock(Qty(1), Now).ErrorValue);
    }

    [Fact]
    public void Quantity_above_one_million_is_rejected_by_every_operation()
    {
        var asset = Open(Asset.MaxOperationQuantity);
        var tooMuch = Qty(Asset.MaxOperationQuantity + 1);

        Assert.IsType<QuantityAboveLimit>(asset.Deposit(tooMuch, Now).ErrorValue);
        Assert.IsType<QuantityAboveLimit>(asset.Block(tooMuch, Now).ErrorValue);
        Assert.IsType<QuantityAboveLimit>(asset.Withdraw(tooMuch, Now).ErrorValue);
        Assert.IsType<QuantityAboveLimit>(asset.Unblock(tooMuch, Now).ErrorValue);
        Assert.IsType<QuantityAboveLimit>(Asset.Open(AssetId.New(), asset.AccountId, asset.Ticker, tooMuch, Now).ErrorValue);
    }

    [Fact]
    public void Default_quantity_is_rejected_by_operations()
    {
        var asset = Open(100);

        Assert.IsType<InvalidQuantity>(asset.Deposit(default, Now).ErrorValue);
        Assert.IsType<InvalidQuantity>(asset.Block(default, Now).ErrorValue);
        Assert.IsType<InvalidQuantity>(asset.Withdraw(default, Now).ErrorValue);
        Assert.IsType<InvalidQuantity>(asset.Unblock(default, Now).ErrorValue);
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
        asset.Deposit(Qty(20), Now);
        asset.Block(Qty(50), Now);
        asset.Withdraw(Qty(10), Now);
        asset.Unblock(Qty(5), Now);

        var restored = Asset.Rehydrate(asset.UncommittedEvents.ToList()).ResultValue;

        Assert.Equal(asset.Id, restored.Id);
        Assert.Equal(asset.AccountId, restored.AccountId);
        Assert.Equal(asset.Ticker, restored.Ticker);
        Assert.Equal(asset.Available, restored.Available);
        Assert.Equal(asset.Blocked, restored.Blocked);
        Assert.Equal(asset.Version, restored.Version);
        Assert.Empty(restored.UncommittedEvents);
    }

    [Fact]
    public void Rehydrate_rejects_empty_history_and_history_without_opening_deposit()
    {
        var asset = Open(100);
        asset.Block(Qty(10), Now);

        Assert.IsType<AssetOpeningEventMissing>(Asset.Rehydrate([]).ErrorValue);
        Assert.IsType<AssetOpeningEventMissing>(Asset.Rehydrate([asset.UncommittedEvents[1]]).ErrorValue);
    }

    [Fact]
    public void Rehydrate_rejects_events_from_another_asset()
    {
        var first = Open(100);
        var other = Open(100);

        Assert.IsType<CorruptAssetHistory>(Asset.Rehydrate([first.UncommittedEvents[0], other.UncommittedEvents[0]]).ErrorValue);
    }

    [Fact]
    public void Restore_from_snapshot_plus_later_events_matches_full_rehydration()
    {
        var asset = Open(100);
        asset.Block(Qty(30), Now);
        var snapshot = asset.ToSnapshot();
        asset.Withdraw(Qty(10), Now);
        asset.Deposit(Qty(5), Now);
        var later = asset.UncommittedEvents.Skip(snapshot.Version).ToList();

        var restored = Asset.Restore(snapshot, later).ResultValue;

        Assert.Equal(asset.Available, restored.Available);
        Assert.Equal(asset.Blocked, restored.Blocked);
        Assert.Equal(asset.Version, restored.Version);
        Assert.Empty(restored.UncommittedEvents);
    }

    [Fact]
    public void Restore_rejects_invalid_snapshot_and_foreign_events()
    {
        var asset = Open(100);
        var other = Open(100);
        var snapshot = asset.ToSnapshot();

        Assert.IsType<CorruptAssetHistory>(Asset.Restore(snapshot with { Available = -1 }, []).ErrorValue);
        Assert.IsType<CorruptAssetHistory>(Asset.Restore(snapshot, [other.UncommittedEvents[0]]).ErrorValue);
    }

    [Fact]
    public void MarkCommitted_clears_uncommitted_events()
    {
        var asset = Open(100);

        asset.MarkCommitted();

        Assert.Empty(asset.UncommittedEvents);
    }
}
