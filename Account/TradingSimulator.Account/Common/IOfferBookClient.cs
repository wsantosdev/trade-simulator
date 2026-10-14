using Moonad;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Common;

public interface IOfferBookClient
{
    // Best ask (lowest sell price) of the ticker; PriceUnavailable when there is no book, no asks or the call failed.
    Task<Result<decimal, IAccountError>> GetBestAskAsync(string ticker, CancellationToken cancellationToken);
}
