using Moonad;

namespace TradeSimulator.Orders.Domain.ValueObjects;

public readonly record struct OrderId
{
    public Guid Guid { get; }

    private OrderId(Guid guid) => Guid = guid;

    public static OrderId New() => new(Guid.CreateVersion7());

    // A repeated client request maps to the same stream, so the event store itself rejects the duplicate.
    public static OrderId For(RequestHash hash)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hash.Value))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new(new Guid(bytes, bigEndian: true));
    }

    public static Result<OrderId, IOrderError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new OrderId(value)
            : new InvalidOrderId();

    public override string ToString() => Guid.ToString();
}
