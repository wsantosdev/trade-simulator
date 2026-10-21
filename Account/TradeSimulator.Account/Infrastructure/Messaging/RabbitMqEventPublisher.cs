using System.Text;
using RabbitMQ.Client;
using TradeSimulator.Account.Infrastructure.Telemetry;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Account.Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher(IConfiguration configuration) : IEventPublisher, IAsyncDisposable
{
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PublishAsync(PendingEvent pendingEvent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        using var activity = AccountTelemetry.Service.StartPublisher("account.events", pendingEvent.EventType, pendingEvent.TraceParent);
        try
        {
            if (_channel is null)
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://guest:guest@localhost:5672/")
                };
                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await _channel.ExchangeDeclareAsync("account.events", ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
            }

            var body = Encoding.UTF8.GetBytes(pendingEvent.Payload);
            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
            if (activity?.Id is { } traceParent)
                properties.Headers = new Dictionary<string, object?> { [ServiceTelemetry.TraceParentHeader] = traceParent };
            await _channel.BasicPublishAsync(
                exchange: "account.events",
                routingKey: pendingEvent.EventType,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
            AccountTelemetry.Service.OutboxPublished("account.events");
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
