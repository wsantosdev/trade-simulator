namespace TradeSimulator.Account.Domain;

public interface IAccountError;

public readonly struct InvalidAccountId : IAccountError;
public readonly struct InvalidDepositAmount : IAccountError;
public readonly struct InvalidWithdrawalAmount : IAccountError;
public readonly struct WithdrawalExceedsAvailableBalance : IAccountError;
public readonly struct InvalidReservationAmount : IAccountError;
public readonly struct ReservationExceedsAvailableBalance : IAccountError;
public readonly struct InvalidReservationId : IAccountError;
public readonly struct ReservationNotFound : IAccountError;
public readonly struct InvalidReleaseAmount : IAccountError;
public readonly struct ReleaseExceedsProvisionedBalance : IAccountError;
public readonly struct InvalidDebitAmount : IAccountError;
public readonly struct DebitExceedsProvisionedBalance : IAccountError;
public readonly struct AccountNotFound : IAccountError;
public readonly struct AccountAlreadyExists : IAccountError;
