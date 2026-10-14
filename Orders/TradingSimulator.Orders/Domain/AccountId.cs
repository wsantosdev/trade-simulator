using Moonad;

namespace TradingSimulator.Orders.Domain;

public readonly record struct AccountId
{
    public Guid Guid { get; }

    private AccountId(Guid guid) => Guid = guid;

    public static Result<AccountId, IOrderError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new AccountId(value)
            : new InvalidAccountId();

    public override string ToString() => Guid.ToString();
}
