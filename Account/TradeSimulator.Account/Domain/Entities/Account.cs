using Moonad;
using TradeSimulator.EventSourcing;
using TradeSimulator.Account.Domain.Events;
using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Domain.Entities;

public sealed class Account : EventSourcedEntity
{
    private Account(AccountId id) => Id = id;

    public AccountId Id { get; }
    public decimal AvailableBalance { get; private set; }
    public decimal ProvisionedBalance { get; private set; }

    // Rebuilt from the events: what is still held per reservation (0 once closed) and the settlements already applied.
    private readonly Dictionary<Guid, decimal> _remaining = [];
    private readonly HashSet<Guid> _settlements = [];

    public static Result<Account, IAccountError> Open(AccountId id)
    {
        if (id.Guid == Guid.Empty || id.Guid.Version != 7)
            return Result<Account, IAccountError>.Error(new InvalidAccountId());

        var account = new Account(id);
        account.RaiseEvent(new AccountOpened(id, DateTimeOffset.UtcNow));
        return account;
    }

    // Rebuilds the current state by replaying the stored events.
    public static Account Rehydrate(AccountId id, IEnumerable<AccountEvent> events)
    {
        var account = new Account(id);
        account.FeedEvents(events);
        return account;
    }

    public Result<Account, IAccountError> Deposit(decimal amount)
    {
        if (amount <= 0 || amount > 1_000_000m)
            return Result<Account, IAccountError>.Error(new InvalidDepositAmount());

        RaiseEvent(new MoneyDeposited(Id, amount, DateTimeOffset.UtcNow));
        return this;
    }

    public Result<Account, IAccountError> Withdraw(decimal amount)
    {
        if (amount <= 0)
            return Result<Account, IAccountError>.Error(new InvalidWithdrawalAmount());
        if (amount > AvailableBalance)
            return Result<Account, IAccountError>.Error(new WithdrawalExceedsAvailableBalance());

        RaiseEvent(new MoneyWithdrawn(Id, amount, DateTimeOffset.UtcNow));
        return this;
    }

    public Result<Account, IAccountError> Reserve(decimal amount, ReservationId? reservationId = null)
    {
        if (reservationId is { } known && _remaining.ContainsKey(known.Guid))
            return this;
        if (amount <= 0)
            return Result<Account, IAccountError>.Error(new InvalidReservationAmount());
        if (amount > AvailableBalance)
            return Result<Account, IAccountError>.Error(new ReservationExceedsAvailableBalance());

        RaiseEvent(new FundsReserved(Id, amount, DateTimeOffset.UtcNow, reservationId));
        return this;
    }

    public Result<Account, IAccountError> Release(decimal amount, ReservationId? reservationId = null)
    {
        if (amount <= 0)
            return Result<Account, IAccountError>.Error(new InvalidReleaseAmount());
        if (amount > ProvisionedBalance)
            return Result<Account, IAccountError>.Error(new ReleaseExceedsProvisionedBalance());

        RaiseEvent(new FundsReleased(Id, amount, DateTimeOffset.UtcNow, reservationId));
        return this;
    }

    public Result<Account, IAccountError> Debit(decimal amount, ReservationId? reservationId = null, Guid? operationId = null)
    {
        if (amount <= 0)
            return Result<Account, IAccountError>.Error(new InvalidDebitAmount());
        if (amount > ProvisionedBalance)
            return Result<Account, IAccountError>.Error(new DebitExceedsProvisionedBalance());

        RaiseEvent(new FundsDebited(Id, amount, DateTimeOffset.UtcNow, reservationId, operationId));
        return this;
    }

    // Debits part of a reservation; when closing, the leftover goes back to the available balance.
    // Repeating an operation, or settling a closed reservation, changes nothing.
    public Result<Account, IAccountError> Settle(ReservationId reservationId, Guid operationId, decimal amount, bool close)
    {
        if (_settlements.Contains(operationId))
            return this;
        if (!_remaining.TryGetValue(reservationId.Guid, out var remaining))
            return Result<Account, IAccountError>.Error(new ReservationNotFound());
        if (remaining == 0)
            return this;

        var debit = Math.Min(amount, remaining);
        var debited = Debit(debit, reservationId, operationId);
        if (debited.IsError)
            return debited;

        return close ? CloseReservation(reservationId) : this;
    }

    // Returns what is still held by the reservation to the available balance. Closed reservations change nothing.
    public Result<Account, IAccountError> CloseReservation(ReservationId reservationId)
    {
        if (!_remaining.TryGetValue(reservationId.Guid, out var remaining))
            return Result<Account, IAccountError>.Error(new ReservationNotFound());

        return remaining == 0 ? this : Release(remaining, reservationId);
    }

    // Events are facts that already passed validation, so applying them only moves the balances.
    protected override void ProcessEvent(IEvent domainEvent)
    {
        switch (domainEvent)
        {
            case MoneyDeposited deposited:
                AvailableBalance += deposited.Amount;
                break;
            case MoneyWithdrawn withdrawn:
                AvailableBalance -= withdrawn.Amount;
                break;
            case FundsReserved reserved:
                AvailableBalance -= reserved.Amount;
                ProvisionedBalance += reserved.Amount;
                if (reserved.ReservationId is { } reservation)
                    _remaining[reservation.Guid] = reserved.Amount;
                break;
            case FundsReleased released:
                ProvisionedBalance -= released.Amount;
                AvailableBalance += released.Amount;
                if (released.ReservationId is { } releasedReservation)
                    _remaining[releasedReservation.Guid] -= released.Amount;
                break;
            case FundsDebited debited:
                ProvisionedBalance -= debited.Amount;
                if (debited.ReservationId is { } debitedReservation)
                    _remaining[debitedReservation.Guid] -= debited.Amount;
                if (debited.OperationId is { } operation)
                    _settlements.Add(operation);
                break;
        }
    }
}
