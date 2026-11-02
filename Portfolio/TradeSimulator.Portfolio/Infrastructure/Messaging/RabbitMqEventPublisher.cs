using System.Text;
using RabbitMQ.Client;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Outbox;
using TradeSimulator.Portfolio.Infrastructure.Telemetry;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

// Delivers the outbox rows to the portfolio events exchange.
public sealed class RabbitMqEventPublisher(IConfiguration configuration) : IOutboxPublisher<PendingEvent>, IAsyncDisposable
{
    public const string ExchangeName = "trade-simulator.portfolio.events";

    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PublishAsync(PendingEvent pending, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        using var activity = PortfolioTelemetry.Service.StartPublisher(ExchangeName, pending.RoutingKey, pending.TraceParent);
        try
        {
            _channel ??= await ConnectAsync(cancellationToken);

            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
            if (activity?.Id is { } id)
                properties.Headers = new Dictionary<string, object?> { [ServiceTelemetry.TraceParentHeader] = id };
            await _channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: pending.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(pending.Payload),
                cancellationToken: cancellationToken);
            PortfolioTelemetry.Service.OutboxPublished(ExchangeName);
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            await ResetAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> ConnectAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://localhost:5672/") };
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        // Bound before the first event so none is dropped while the projection consumer is still starting.
        await channel.QueueDeclareAsync(
            AssetProjectionConsumer.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(AssetProjectionConsumer.Queue, ExchangeName, AssetChanged.RoutingKey, cancellationToken: cancellationToken);
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _gate.Dispose();
    }

    private async Task ResetAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
        _channel = null;
        _connection = null;
    }
}
