namespace TradeSimulator.Account.Infrastructure.Messaging;

public interface IResponseOutbox
{
    Task<IReadOnlyList<PendingResponse>> ReadPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(long position, CancellationToken cancellationToken);
}
