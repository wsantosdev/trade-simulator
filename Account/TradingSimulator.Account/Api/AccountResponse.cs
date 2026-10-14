using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Api;

public sealed record AccountResponse(Guid AccountId, decimal AvailableBalance, decimal ProvisionedBalance)
{
    public static AccountResponse From(Domain.Account account) =>
        new(account.Id.Guid, account.AvailableBalance, account.ProvisionedBalance);
}
