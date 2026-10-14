using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using TradingSimulator.Quotes.Common;
using TradingSimulator.Quotes.Infrastructure.Serialization;

namespace TradingSimulator.Quotes.Infrastructure.Messaging;

public sealed class RabbitMqQuoteInterestPublisher(IConfiguration configuration) : IQuoteInterestPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(EventEnvelope<QuoteInterestRenewed> envelope, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, QuoteJson.Options));
            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Transient };
            await channel.BasicPublishAsync(
                QuoteTopology.InterestExchange, "interest", mandatory: false, properties, body, cancellationToken);
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
            QuoteTopology.InterestExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        // Declared on both sides with identical arguments so that the first interest is not lost if Quotes starts first.
        await _channel.QueueDeclareAsync(
            QuoteTopology.InterestQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-message-ttl"] = QuoteTopology.InterestMessageTtlMilliseconds },
            cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(
            QuoteTopology.InterestQueue, QuoteTopology.InterestExchange, "#", cancellationToken: cancellationToken);
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
