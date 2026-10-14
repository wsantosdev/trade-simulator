using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Api;

public static class OrderErrorHttpResults
{
    public static IResult ToHttpResult(this IOrderError error)
    {
        var (status, title, detail) = error switch
        {
            InvalidOrderId => (StatusCodes.Status400BadRequest, "Invalid order identifier", "Order identifiers must be non-empty UUID version 7 values."),
            InvalidAccountId => (StatusCodes.Status400BadRequest, "Invalid account identifier", "Account identifiers must be non-empty UUID version 7 values."),
            InvalidTicker => (StatusCodes.Status400BadRequest, "Invalid ticker", "A ticker must have four letters followed by the digit 3, 4 or 5 (for example PETR4)."),
            InvalidRequestHash => (StatusCodes.Status400BadRequest, "Invalid request hash", "The request hash must be a SHA-256 value in hexadecimal."),
            InvalidOrderSide => (StatusCodes.Status400BadRequest, "Invalid side", "The side must be Buy or Sell."),
            InvalidOrderType => (StatusCodes.Status400BadRequest, "Invalid type", "The type must be Market or Limit."),
            InvalidSortField => (StatusCodes.Status400BadRequest, "Invalid sort field", "The requested sort field is not supported."),
            InvalidQuantity => (StatusCodes.Status422UnprocessableEntity, "Invalid quantity", "The quantity must be an integer greater than zero."),
            InvalidLimitPrice => (StatusCodes.Status422UnprocessableEntity, "Invalid limit price", "A limit order requires a price greater than zero."),
            PriceNotAllowedForMarketOrder => (StatusCodes.Status422UnprocessableEntity, "Price not allowed", "A market order must not carry a price."),
            ReplaceNotAllowedForMarketOrder => (StatusCodes.Status422UnprocessableEntity, "Replace not allowed", "A market order cannot have its price replaced."),
            InvalidStateTransition => (StatusCodes.Status409Conflict, "Invalid order state", "The operation is not allowed in the current order state."),
            ConcurrencyConflict => (StatusCodes.Status409Conflict, "Order conflict", "The order was changed concurrently. Try again."),
            OrderNotFound => (StatusCodes.Status404NotFound, "Order not found", "The requested order does not exist."),
            OrderAlreadyExists => (StatusCodes.Status409Conflict, "Order conflict", "The order already exists."),
            _ => (StatusCodes.Status500InternalServerError, "Order state error", "The order could not be processed.")
        };

        return Results.Problem(statusCode: status, title: title, detail: detail);
    }
}
