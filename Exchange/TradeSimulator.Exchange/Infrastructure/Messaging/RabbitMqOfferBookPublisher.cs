using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using TradeSimulator.Exchange.Domain.Enums;
using TradeSimulator.Exchange.Domain.ValueObjects;
using TradeSimulator.Exchange.Infrastructure.Serialization;
using TradeSimulator.Exchange.Processors.OfferBook;

namespace TradeSimulator.Exchange.Infrastructure.Messaging;

public sealed class RabbitMqOfferBookPublisher(IConfiguration configuration) : IOfferBookPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public Task PublishSnapshotAsync(BookSnapshot snapshot, CancellationToken cancellationToken)
    {
        var message = new OfferBookSnapshot(
            snapshot.Ticker.Value, snapshot.Sequence, snapshot.Phase, ToLevels(snapshot.Bids), ToLevels(snapshot.Asks), snapshot.Timestamp);
        return PublishAsync(EventEnvelope.Of(message), message.Ticker, cancellationToken);
    }

    public Task PublishDeltaAsync(BookDelta delta, CancellationToken cancellationToken)
    {
        var message = new OfferBookDelta(
            delta.Ticker.Value, delta.Sequence, delta.PreviousSequence, delta.Phase, [.. delta.Changes.Select(ToChange)], delta.Timestamp);
        return PublishAsync(EventEnvelope.Of(message), message.Ticker, cancellationToken);
    }

    private static IReadOnlyList<OfferBookLevel> ToLevels(IReadOnlyList<BookLevel> levels) =>
        [.. levels.Select(level => new OfferBookLevel(level.Price.Value, level.Quantity.Value, level.Orders))];

    private static OfferBookChange ToChange(BookChange change) => new(
        change.Side == OrderSide.Buy ? OfferBookSide.Bid : OfferBookSide.Ask, change.Price.Value, change.Quantity.Value, change.Orders);

    private async Task PublishAsync<T>(EventEnvelope<T> envelope, string ticker, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, MessageJson.Options));
            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Transient };
            await channel.BasicPublishAsync(
                OfferBookTopology.EventsExchange, OfferBookTopology.RoutingKey(ticker), mandatory: false, properties, body, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ResetAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await ResetAsync();
        var factory = new ConnectionFactory
        {
            Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://localhost:5672/")
        };
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _channel.ExchangeDeclareAsync(
            OfferBookTopology.EventsExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        return _channel;
    }

    private async Task ResetAsync()
    {
        var channel = _channel;
        var connection = _connection;
        _channel = null;
        _connection = null;
        try
        {
            if (channel is not null)
                await channel.DisposeAsync();
            if (connection is not null)
                await connection.DisposeAsync();
        }
        catch (Exception)
        {
            // The broken connection is being discarded; nothing else to release.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _gate.Dispose();
    }
}
