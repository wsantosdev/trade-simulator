using Moonad;
using TradingSimulator.OfferBook.Common;
using TradingSimulator.OfferBook.Domain;
using TradingSimulator.OfferBook.Domain.ValueObjects;

namespace TradingSimulator.OfferBook.Features.GetOfferBook;

// Inbound port.
public interface IGetOfferBook
{
    Result<OfferBookSnapshot, IOfferBookError> Execute(string ticker);
}

public sealed class GetOfferBookHandler(LatestOfferBooks latest) : IGetOfferBook
{
    public Result<OfferBookSnapshot, IOfferBookError> Execute(string ticker)
    {
        var parsed = Ticker.TryCreate(ticker);
        if (parsed.IsError)
            return Result<OfferBookSnapshot, IOfferBookError>.Error(parsed.ErrorValue);

        var book = latest.Find(parsed.ResultValue);
        return book is null
            ? Result<OfferBookSnapshot, IOfferBookError>.Error(new OfferBookNotAvailable())
            : Result<OfferBookSnapshot, IOfferBookError>.Ok(book);
    }
}
