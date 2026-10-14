using TradingSimulator.OfferBook.Processors.OfferBookInterest;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using TradingSimulator.OfferBook.Common;
using TradingSimulator.OfferBook.Infrastructure.Serialization;

namespace TradingSimulator.OfferBook.Infrastructure.Messaging;

public sealed class RabbitMqOfferBookInterestPublisher(IConfiguration configuration)
    : IOfferBookInterestPublisher, IOfferBookSnapshotRequestPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public Task PublishAsync(EventEnvelope<OfferBookInterestRenewed> envelope, CancellationToken cancellationToken) =>
        SendAsync(envelope, cancellationToken);

    public Task PublishAsync(EventEnvelope<OfferBookSnapshotRequested> envelope, CancellationToken cancellationToken) =>
        SendAsync(envelope, cancellationToken);

    private async Task SendAsync<T>(EventEnvelope<T> envelope, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, OfferBookJson.Options));
            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Transient };
            await channel.BasicPublishAsync(
                OfferBookTopology.InterestExchange, OfferBookTopology.InterestRoutingKey, mandatory: false, properties, body, cancellationToken);
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
            OfferBookTopology.InterestExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        // Declared on both sides with identical arguments so that the first interest is not lost if OfferBook starts first.
        await _channel.QueueDeclareAsync(
            OfferBookTopology.InterestQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-message-ttl"] = OfferBookTopology.InterestMessageTtlMilliseconds },
            cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(
            OfferBookTopology.InterestQueue, OfferBookTopology.InterestExchange, "#", cancellationToken: cancellationToken);
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
