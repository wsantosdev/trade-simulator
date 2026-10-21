using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Infrastructure.Http;

public sealed record AccountResponse(Guid AccountId, decimal AvailableBalance, decimal ProvisionedBalance)
{
    public static AccountResponse From(Domain.Entities.Account account) =>
        new(account.Id.Guid, account.AvailableBalance, account.ProvisionedBalance);
}
