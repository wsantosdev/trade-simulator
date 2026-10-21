using System.Text;
using RabbitMQ.Client;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Orders.Infrastructure.Telemetry;

namespace TradeSimulator.Orders.Infrastructure.Messaging;

public sealed class RabbitMqCommandPublisher(IConfiguration configuration) : ICommandPublisher, IAsyncDisposable
{
    public const string ExchangeName = "trade-simulator-order-commands";

    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PublishAsync(PendingEvent pendingEvent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        using var activity = OrdersTelemetry.Service.StartPublisher(ExchangeName, pendingEvent.EventType, pendingEvent.TraceParent);
        try
        {
            if (_channel is null)
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://localhost:5672/")
                };
                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await _channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
            }

            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
            if (activity?.Id is { } traceParent)
                properties.Headers = new Dictionary<string, object?> { [ServiceTelemetry.TraceParentHeader] = traceParent };
            await _channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: pendingEvent.EventType,
                mandatory: false,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(pendingEvent.Payload),
                cancellationToken: cancellationToken);
            OrdersTelemetry.Service.OutboxPublished(ExchangeName);
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
