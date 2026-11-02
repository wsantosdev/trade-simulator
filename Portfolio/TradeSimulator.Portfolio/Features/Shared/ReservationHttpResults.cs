namespace TradeSimulator.Portfolio.Features.Shared;

public static class ReservationHttpResults
{
    public static IResult ToHttpResult(this ReservationFailure failure) => failure switch
    {
        ReservationFailure.InsufficientQuantity => Json(failure, StatusCodes.Status422UnprocessableEntity),
        ReservationFailure.AssetNotFound => Json(failure, StatusCodes.Status404NotFound),
        ReservationFailure.Conflict => Json(failure, StatusCodes.Status409Conflict),
        _ => Json(failure, StatusCodes.Status400BadRequest)
    };

    private static IResult Json(ReservationFailure failure, int status) =>
        Results.Json(new { reserved = false, reason = failure.ToString() }, statusCode: status);
}
