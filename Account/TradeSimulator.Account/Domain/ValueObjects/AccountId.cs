namespace TradeSimulator.Account.Domain.ValueObjects;

using Moonad;

public readonly record struct AccountId
{
    public Guid Guid { get; }

    private AccountId(Guid guid) => Guid = guid;

    public static AccountId New() => new(Guid.CreateVersion7());

    public static Result<AccountId, IAccountError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new AccountId(value)
            : new InvalidAccountId();

    public override string ToString() => Guid.ToString();
}
