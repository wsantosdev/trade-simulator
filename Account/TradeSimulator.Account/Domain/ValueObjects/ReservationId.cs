namespace TradeSimulator.Account.Domain.ValueObjects;

using Moonad;

// Opaque key chosen by the caller of a funds reservation; Account attaches no meaning to it.
public readonly record struct ReservationId
{
    public Guid Guid { get; }

    private ReservationId(Guid guid) => Guid = guid;

    public static Result<ReservationId, IAccountError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new ReservationId(value)
            : new InvalidReservationId();

    public override string ToString() => Guid.ToString();
}
