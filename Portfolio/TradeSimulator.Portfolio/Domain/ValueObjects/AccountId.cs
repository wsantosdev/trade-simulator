using Moonad;

namespace TradeSimulator.Portfolio.Domain.ValueObjects;

public readonly record struct AccountId
{
    public Guid Guid { get; }

    private AccountId(Guid guid) => Guid = guid;

    public static Result<AccountId, IPortfolioError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new AccountId(value)
            : new InvalidAccountId();

    public override string ToString() => Guid.ToString();
}
