using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Api;

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
            InvalidOrderId => (StatusCodes.Status400BadRequest, "Invalid order identifier", "Order identifiers must be non-empty UUID version 7 values."),
            InvalidReleaseAmount => (StatusCodes.Status422UnprocessableEntity, "Invalid release", "A release must be greater than zero."),
            ReleaseExceedsProvisionedBalance => (StatusCodes.Status422UnprocessableEntity, "Insufficient provisioned balance", "A release cannot exceed the provisioned balance."),
            InvalidDebitAmount => (StatusCodes.Status422UnprocessableEntity, "Invalid debit", "A debit must be greater than zero."),
            DebitExceedsProvisionedBalance => (StatusCodes.Status422UnprocessableEntity, "Insufficient provisioned balance", "A debit cannot exceed the provisioned balance."),
            PriceUnavailable => (StatusCodes.Status422UnprocessableEntity, "Price unavailable", "The market price could not be determined."),
            AccountBalanceLimitExceeded => (StatusCodes.Status422UnprocessableEntity, "Account balance limit exceeded", "The operation would exceed the maximum account balance."),
            AccountNotFound => (StatusCodes.Status404NotFound, "Account not found", "The requested account does not exist."),
            AccountAlreadyExists or AccountAlreadyOpened => (StatusCodes.Status409Conflict, "Account conflict", "The account already exists or has already been opened."),
            SnapshotAccountMismatch or InvalidSnapshotBalance or AccountOpeningEventMissing or EventAccountMismatch or
                InvalidPersistedDeposit or InvalidPersistedWithdrawal or InvalidPersistedReservation or InvalidPersistedRelease or InvalidPersistedDebit or UnsupportedAccountEvent or
                NegativeAccountBalance or CorruptAccountHistory or AccountPersistenceFailure =>
                (StatusCodes.Status500InternalServerError, "Account state error", "The account state could not be processed."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected account error", "The request could not be processed.")
        };

        return Results.Problem(statusCode: status, title: title, detail: detail);
    }
}
