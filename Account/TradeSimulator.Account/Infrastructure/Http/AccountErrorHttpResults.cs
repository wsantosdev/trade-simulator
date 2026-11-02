using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.Entities;

namespace TradeSimulator.Account.Infrastructure.Http;

public static class AccountErrorHttpResults
{
    public static IResult ToHttpResult(this IAccountError error)
    {
        var (status, title, detail) = error switch
        {
            InvalidAccountId => (StatusCodes.Status400BadRequest, "Invalid account identifier", "Account identifiers must be non-empty UUID version 7 values."),
            InvalidDepositAmount => (StatusCodes.Status422UnprocessableEntity, "Invalid deposit", "A deposit must be greater than zero and at most $1,000,000."),
            InvalidWithdrawalAmount => (StatusCodes.Status422UnprocessableEntity, "Invalid withdrawal", "A withdrawal must be greater than zero."),
            WithdrawalExceedsAvailableBalance => (StatusCodes.Status422UnprocessableEntity, "Insufficient available balance", "A withdrawal cannot exceed the available balance."),
            InvalidReservationAmount => (StatusCodes.Status422UnprocessableEntity, "Invalid reservation", "A reservation must be greater than zero."),
            ReservationExceedsAvailableBalance => (StatusCodes.Status422UnprocessableEntity, "Insufficient available balance", "A reservation cannot exceed the available balance."),
            InvalidReservationId => (StatusCodes.Status400BadRequest, "Invalid reservation identifier", "Reservation identifiers must be non-empty UUID version 7 values."),
            ReservationNotFound => (StatusCodes.Status404NotFound, "Reservation not found", "The requested reservation does not exist."),
            InvalidReleaseAmount => (StatusCodes.Status422UnprocessableEntity, "Invalid release", "A release must be greater than zero."),
            ReleaseExceedsProvisionedBalance => (StatusCodes.Status422UnprocessableEntity, "Insufficient provisioned balance", "A release cannot exceed the provisioned balance."),
            InvalidDebitAmount => (StatusCodes.Status422UnprocessableEntity, "Invalid debit", "A debit must be greater than zero."),
            DebitExceedsProvisionedBalance => (StatusCodes.Status422UnprocessableEntity, "Insufficient provisioned balance", "A debit cannot exceed the provisioned balance."),
            AccountNotFound => (StatusCodes.Status404NotFound, "Account not found", "The requested account does not exist."),
            AccountAlreadyExists => (StatusCodes.Status409Conflict, "Account conflict", "The account already exists or has already been opened."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected account error", "The request could not be processed.")
        };

        return Results.Problem(statusCode: status, title: title, detail: detail);
    }
}
