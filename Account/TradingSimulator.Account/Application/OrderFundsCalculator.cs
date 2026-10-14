using Moonad;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Application;

public static class OrderFundsCalculator
{
    public const decimal MarketMargin = 1.05m;

    public static Result<decimal, IAccountError> ForLimit(long quantity, decimal price) =>
        Compute(quantity, price, 1m, RoundUp);

    public static Result<decimal, IAccountError> ForMarket(long quantity, decimal bestAsk) =>
        Compute(quantity, bestAsk, MarketMargin, RoundUp);

    // Value of an execution, to the cent.
    public static Result<decimal, IAccountError> ForFill(long quantity, decimal price) =>
        Compute(quantity, price, 1m, value => Math.Round(value, 2, MidpointRounding.AwayFromZero));

    private static Result<decimal, IAccountError> Compute(long quantity, decimal price, decimal factor, Func<decimal, decimal> round)
    {
        if (quantity <= 0 || price <= 0)
            return Result<decimal, IAccountError>.Error(new InvalidReservationAmount());

        try
        {
            var amount = round(checked(quantity * price * factor));
            return amount > 0
                ? amount
                : Result<decimal, IAccountError>.Error(new InvalidReservationAmount());
        }
        catch (OverflowException)
        {
            return Result<decimal, IAccountError>.Error(new AccountBalanceLimitExceeded());
        }
    }

    private static decimal RoundUp(decimal value) => Math.Ceiling(value * 100m) / 100m;
}
