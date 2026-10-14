using TradingSimulator.Portfolio.Domain;

namespace TradingSimulator.Portfolio.Api;

public static class PortfolioErrorHttpResults
{
    public static IResult ToHttpResult(this IPortfolioError error)
    {
        var (status, title, detail) = error switch
        {
            InvalidAccountId => (StatusCodes.Status400BadRequest, "Invalid account identifier", "Account identifiers must be non-empty UUID version 7 values."),
            InvalidTicker => (StatusCodes.Status400BadRequest, "Invalid ticker", "A ticker must have four letters followed by the digit 3, 4 or 5 (for example PETR4)."),
            InvalidSortField => (StatusCodes.Status400BadRequest, "Invalid sort field", "The sort field must be ticker, total, available or blocked."),
            AssetNotFound => (StatusCodes.Status404NotFound, "Asset not found", "The account does not hold this asset."),
            _ => (StatusCodes.Status500InternalServerError, "Portfolio error", "The request could not be processed.")
        };

        return Results.Problem(statusCode: status, title: title, detail: detail);
    }
}
