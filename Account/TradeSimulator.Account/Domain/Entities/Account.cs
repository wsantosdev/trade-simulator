using Moonad;
using TradeSimulator.Account.Domain.Events;
using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Domain.Entities;

public sealed class Account
{
    private const decimal MaximumPersistedBalance = 9_999_999_999_999_999.99m;
    private readonly List<AccountEvent> _uncommittedEvents = [];
    private bool _isOpened;

    private Account(AccountId id) => Id = id;

    public AccountId Id { get; }
    public decimal AvailableBalance { get; private set; }
    public decimal ProvisionedBalance { get; private set; }
    public IReadOnlyList<AccountEvent> UncommittedEvents => _uncommittedEvents;

    public static Result<Account, IAccountError> Open(AccountId id)
    {
        if (id.Guid == Guid.Empty || id.Guid.Version != 7)
            return Result<Account, IAccountError>.Error(new InvalidAccountId());

        var account = new Account(id);
        return account.Raise(new AccountOpened(id, DateTimeOffset.UtcNow));
    }

    public static Result<Account, IAccountError> Rehydrate(AccountId id, IEnumerable<AccountEvent> events)
    {
        if (id.Guid == Guid.Empty || id.Guid.Version != 7)
            return Result<Account, IAccountError>.Error(new InvalidAccountId());

        var account = new Account(id);
        foreach (var @event in events)
        {
            var applied = account.Apply(@event);
            if (applied.IsError)
                return Result<Account, IAccountError>.Error(applied.ErrorValue);
        }

        return account._isOpened
            ? account
            : Result<Account, IAccountError>.Error(new AccountOpeningEventMissing());
    }

    public static Result<Account, IAccountError> Rehydrate(
        AccountId id,
        AccountSnapshot snapshot,
        IEnumerable<AccountEvent> events)
    {
        if (id.Guid == Guid.Empty || id.Guid.Version != 7)
            return Result<Account, IAccountError>.Error(new InvalidAccountId());
        if (snapshot.AccountId != id)
            return Result<Account, IAccountError>.Error(new SnapshotAccountMismatch());
        if (snapshot.AvailableBalance < 0 || snapshot.ProvisionedBalance < 0 ||
            snapshot.AvailableBalance > MaximumPersistedBalance || snapshot.ProvisionedBalance > MaximumPersistedBalance)
            return Result<Account, IAccountError>.Error(new InvalidSnapshotBalance());

        var account = new Account(id)
        {
            AvailableBalance = snapshot.AvailableBalance,
            ProvisionedBalance = snapshot.ProvisionedBalance,
            _isOpened = true
        };
        foreach (var @event in events)
        {
            var applied = account.Apply(@event);
            if (applied.IsError)
                return Result<Account, IAccountError>.Error(applied.ErrorValue);
        }

        return account;
    }

    public Result<AccountSnapshot, IAccountError> CreateSnapshot() =>
        _isOpened
            ? new AccountSnapshot(Id, AvailableBalance, ProvisionedBalance)
            : Result<AccountSnapshot, IAccountError>.Error(new AccountOpeningEventMissing());

    public Result<Account, IAccountError> Deposit(decimal amount)
    {
        if (amount <= 0 || amount > 1_000_000m)
            return Result<Account, IAccountError>.Error(new InvalidDepositAmount());
        if (AvailableBalance > MaximumPersistedBalance - amount)
            return Result<Account, IAccountError>.Error(new AccountBalanceLimitExceeded());

        return Raise(new MoneyDeposited(Id, amount, DateTimeOffset.UtcNow));
    }

    public Result<Account, IAccountError> Withdraw(decimal amount)
    {
        if (amount <= 0)
            return Result<Account, IAccountError>.Error(new InvalidWithdrawalAmount());
        if (amount > AvailableBalance)
            return Result<Account, IAccountError>.Error(new WithdrawalExceedsAvailableBalance());

        return Raise(new MoneyWithdrawn(Id, amount, DateTimeOffset.UtcNow));
    }

    public Result<Account, IAccountError> Reserve(decimal amount, OrderId? orderId = null)
    {
        if (amount <= 0)
            return Result<Account, IAccountError>.Error(new InvalidReservationAmount());
        if (amount > AvailableBalance)
            return Result<Account, IAccountError>.Error(new ReservationExceedsAvailableBalance());
        if (ProvisionedBalance > MaximumPersistedBalance - amount)
            return Result<Account, IAccountError>.Error(new AccountBalanceLimitExceeded());

        return Raise(new FundsReserved(Id, amount, DateTimeOffset.UtcNow, orderId));
    }

    public Result<Account, IAccountError> Release(decimal amount, OrderId? orderId = null)
    {
        if (amount <= 0)
            return Result<Account, IAccountError>.Error(new InvalidReleaseAmount());
        if (amount > ProvisionedBalance)
            return Result<Account, IAccountError>.Error(new ReleaseExceedsProvisionedBalance());
        if (AvailableBalance > MaximumPersistedBalance - amount)
            return Result<Account, IAccountError>.Error(new AccountBalanceLimitExceeded());

        return Raise(new FundsReleased(Id, amount, DateTimeOffset.UtcNow, orderId));
    }

    public Result<Account, IAccountError> Debit(decimal amount, OrderId? orderId = null)
    {
        if (amount <= 0)
            return Result<Account, IAccountError>.Error(new InvalidDebitAmount());
        if (amount > ProvisionedBalance)
            return Result<Account, IAccountError>.Error(new DebitExceedsProvisionedBalance());

        return Raise(new FundsDebited(Id, amount, DateTimeOffset.UtcNow, orderId));
    }

    public Result<Account, IAccountError> Apply(AccountEvent @event)
    {
        if (@event.AccountId != Id)
            return Result<Account, IAccountError>.Error(new EventAccountMismatch());

        switch (@event)
        {
            case AccountOpened when !_isOpened:
                _isOpened = true;
                break;
            case AccountOpened:
                return Result<Account, IAccountError>.Error(new AccountAlreadyOpened());
            case MoneyDeposited deposited:
                if (!_isOpened)
                    return Result<Account, IAccountError>.Error(new AccountOpeningEventMissing());
                if (deposited.Amount <= 0 || deposited.Amount > 1_000_000m)
                    return Result<Account, IAccountError>.Error(new InvalidPersistedDeposit());
                if (AvailableBalance > MaximumPersistedBalance - deposited.Amount)
                    return Result<Account, IAccountError>.Error(new AccountBalanceLimitExceeded());
                AvailableBalance += deposited.Amount;
                break;
            case MoneyWithdrawn withdrawn:
                if (!_isOpened)
                    return Result<Account, IAccountError>.Error(new AccountOpeningEventMissing());
                if (withdrawn.Amount <= 0 || withdrawn.Amount > AvailableBalance)
                    return Result<Account, IAccountError>.Error(new InvalidPersistedWithdrawal());
                AvailableBalance -= withdrawn.Amount;
                break;
            case FundsReserved reserved:
                if (!_isOpened)
                    return Result<Account, IAccountError>.Error(new AccountOpeningEventMissing());
                if (reserved.Amount <= 0 || reserved.Amount > AvailableBalance)
                    return Result<Account, IAccountError>.Error(new InvalidPersistedReservation());
                if (ProvisionedBalance > MaximumPersistedBalance - reserved.Amount)
                    return Result<Account, IAccountError>.Error(new AccountBalanceLimitExceeded());
                AvailableBalance -= reserved.Amount;
                ProvisionedBalance += reserved.Amount;
                break;
            case FundsReleased released:
                if (!_isOpened)
                    return Result<Account, IAccountError>.Error(new AccountOpeningEventMissing());
                if (released.Amount <= 0 || released.Amount > ProvisionedBalance)
                    return Result<Account, IAccountError>.Error(new InvalidPersistedRelease());
                if (AvailableBalance > MaximumPersistedBalance - released.Amount)
                    return Result<Account, IAccountError>.Error(new AccountBalanceLimitExceeded());
                ProvisionedBalance -= released.Amount;
                AvailableBalance += released.Amount;
                break;
            case FundsDebited debited:
                if (!_isOpened)
                    return Result<Account, IAccountError>.Error(new AccountOpeningEventMissing());
                if (debited.Amount <= 0 || debited.Amount > ProvisionedBalance)
                    return Result<Account, IAccountError>.Error(new InvalidPersistedDebit());
                ProvisionedBalance -= debited.Amount;
                break;
            default:
                return Result<Account, IAccountError>.Error(new UnsupportedAccountEvent());
        }

        if (AvailableBalance < 0 || ProvisionedBalance < 0)
            return Result<Account, IAccountError>.Error(new NegativeAccountBalance());

        return this;
    }

    public void MarkCommitted() => _uncommittedEvents.Clear();

    private Result<Account, IAccountError> Raise(AccountEvent @event)
    {
        var applied = Apply(@event);
        if (applied.IsError)
            return applied;

        _uncommittedEvents.Add(@event);
        return this;
    }
}
