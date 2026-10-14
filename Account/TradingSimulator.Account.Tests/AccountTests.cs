using Moonad;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Xunit;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Infrastructure.Http;

namespace TradingSimulator.Account.Tests;

public sealed class AccountTests
{
    [Fact]
    public void Open_starts_with_zero_balances()
    {
        var account = Value(Domain.Entities.Account.Open(AccountId.New()));

        Assert.Equal(0m, account.AvailableBalance);
        Assert.Equal(0m, account.ProvisionedBalance);
    }

    [Fact]
    public void New_account_ids_use_uuid_version_seven()
    {
        Assert.Equal(7, AccountId.New().Guid.Version);
    }

    [Fact]
    public void Account_id_creation_returns_a_typed_error_for_non_v7_guids()
    {
        var result = AccountId.TryCreate(Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.IsType<InvalidAccountId>(result.ErrorValue);
        Assert.IsType<InvalidAccountId>(Domain.Entities.Account.Open(default).ErrorValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_000.01)]
    public void Deposit_returns_a_typed_error_outside_limits(decimal amount)
    {
        var account = OpenAccount();

        var result = account.Deposit(amount);

        Assert.True(result.IsError);
        Assert.IsType<InvalidDepositAmount>(result.ErrorValue);
        Assert.Equal(0m, account.AvailableBalance);
    }

    [Fact]
    public void Withdrawal_and_reservation_validation_return_distinct_typed_errors()
    {
        var account = OpenAccount();

        Assert.IsType<InvalidWithdrawalAmount>(account.Withdraw(0).ErrorValue);
        Assert.IsType<InvalidReservationAmount>(account.Reserve(0).ErrorValue);
        Assert.Equal(0m, account.AvailableBalance);
        Assert.Empty(account.UncommittedEvents.Skip(1));
    }

    [Fact]
    public void Deposit_accepts_maximum_and_withdrawal_can_zero_available_balance()
    {
        var account = OpenAccount();
        Assert.True(account.Deposit(1_000_000m).IsOk);

        Assert.True(account.Withdraw(1_000_000m).IsOk);

        Assert.Equal(0m, account.AvailableBalance);
        Assert.Equal(0m, account.ProvisionedBalance);
    }

    [Fact]
    public void Withdrawal_cannot_use_provisioned_funds()
    {
        var account = OpenAccount();
        Assert.True(account.Deposit(100m).IsOk);
        Assert.True(account.Reserve(75m).IsOk);

        var rejected = account.Withdraw(26m);
        Assert.IsType<WithdrawalExceedsAvailableBalance>(rejected.ErrorValue);
        Assert.True(account.Withdraw(25m).IsOk);

        Assert.Equal(0m, account.AvailableBalance);
        Assert.Equal(75m, account.ProvisionedBalance);
    }

    [Fact]
    public void Reservation_cannot_exceed_available_balance()
    {
        var account = OpenAccount();
        Assert.True(account.Deposit(10m).IsOk);

        var rejected = account.Reserve(10.01m);

        Assert.IsType<ReservationExceedsAvailableBalance>(rejected.ErrorValue);
        Assert.Equal(10m, account.AvailableBalance);
        Assert.Equal(0m, account.ProvisionedBalance);
    }

    [Fact]
    public void Rehydration_replays_events_into_the_same_balances()
    {
        var account = OpenAccount();
        Assert.True(account.Deposit(150m).IsOk);
        Assert.True(account.Reserve(50m).IsOk);
        Assert.True(account.Withdraw(30m).IsOk);

        var rehydrated = Value(Domain.Entities.Account.Rehydrate(account.Id, account.UncommittedEvents));

        Assert.Equal(70m, rehydrated.AvailableBalance);
        Assert.Equal(50m, rehydrated.ProvisionedBalance);
    }

    [Fact]
    public void Rehydration_applies_only_events_after_the_snapshot()
    {
        var account = OpenAccount();
        Assert.True(account.Deposit(150m).IsOk);
        Assert.True(account.Reserve(50m).IsOk);
        var snapshot = Value(account.CreateSnapshot());

        Assert.True(account.Withdraw(30m).IsOk);
        var rehydrated = Value(Domain.Entities.Account.Rehydrate(account.Id, snapshot, [account.UncommittedEvents[^1]]));

        Assert.Equal(70m, rehydrated.AvailableBalance);
        Assert.Equal(50m, rehydrated.ProvisionedBalance);
        Assert.Empty(rehydrated.UncommittedEvents);
    }

    [Fact]
    public void Rehydration_returns_a_typed_error_for_a_snapshot_from_another_account()
    {
        var account = OpenAccount();
        var other = OpenAccount();

        var result = Domain.Entities.Account.Rehydrate(account.Id, Value(other.CreateSnapshot()), []);

        Assert.IsType<SnapshotAccountMismatch>(result.ErrorValue);
    }

    [Fact]
    public void Api_maps_typed_validation_and_missing_account_errors_to_http_statuses()
    {
        Assert.Equal(StatusCodes.Status422UnprocessableEntity,
            ((IStatusCodeHttpResult)new InvalidDepositAmount().ToHttpResult()).StatusCode);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity,
            ((IStatusCodeHttpResult)new WithdrawalExceedsAvailableBalance().ToHttpResult()).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound,
            ((IStatusCodeHttpResult)new AccountNotFound().ToHttpResult()).StatusCode);
    }

    [Fact]
    public void Account_error_types_are_propertyless_readonly_structs()
    {
        var errorType = typeof(InvalidDepositAmount);

        Assert.True(errorType.IsValueType);
        Assert.True(errorType.IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
        Assert.Empty(errorType.GetProperties());
    }

    private static Domain.Entities.Account OpenAccount() => Value(Domain.Entities.Account.Open(AccountId.New()));

    private static T Value<T>(Result<T, IAccountError> result) where T : notnull
    {
        Assert.True(result.IsOk);
        return result.ResultValue;
    }
}
