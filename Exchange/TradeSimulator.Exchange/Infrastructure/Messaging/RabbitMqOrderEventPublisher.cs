using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Exchange.Infrastructure.Telemetry;
using TradeSimulator.Exchange.Domain.Events;
using TradeSimulator.Exchange.Infrastructure.Serialization;
using TradeSimulator.Exchange.Processors.OrderCommands;

namespace TradeSimulator.Exchange.Infrastructure.Messaging;

public sealed class RabbitMqOrderEventPublisher(IConfiguration configuration) : IOrderEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(Guid eventId, OrderEvent orderEvent, CancellationToken cancellationToken)
    {
        var message = ToMessage(orderEvent);
        var eventType = message.GetType().Name.Replace("Message", string.Empty, StringComparison.Ordinal);
        var envelope = new EventEnvelope<object>(eventId, eventType, message);
        await _gate.WaitAsync(cancellationToken);
        using var activity = ExchangeTelemetry.Service.StartPublisher(OrderTopology.EventsExchange, envelope.EventType, null);
        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, MessageJson.Options));
            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
            if (activity?.Id is { } traceParent)
                properties.Headers = new Dictionary<string, object?> { [ServiceTelemetry.TraceParentHeader] = traceParent };
            await channel.BasicPublishAsync(
                OrderTopology.EventsExchange, envelope.EventType, mandatory: false, properties, body, cancellationToken);
            ExchangeTelemetry.Service.OutboxPublished(OrderTopology.EventsExchange);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
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

    private static object ToMessage(OrderEvent orderEvent) => orderEvent switch
    {
        OrderAcked e => new OrderAckedMessage(e.OrderId),
        OrderRejected e => new OrderRejectedMessage(e.OrderId, e.Reason),
        OrderPartiallyFilled e => new OrderPartiallyFilledMessage(e.OrderId, e.Executed.Value, e.Remaining.Value, e.ExecutionPrice.Value),
        OrderFilled e => new OrderFilledMessage(e.OrderId, e.Executed.Value, e.ExecutionPrice.Value),
        OrderCancelled e => new OrderCancelledMessage(e.OrderId, e.Executed.Value, e.Remaining.Value),
        OrderReplaced e => new OrderReplacedMessage(e.OrderId, e.Executed.Value, e.Remaining.Value),
        OrderCancelRejected e => new OrderCancelRejectedMessage(e.OrderId),
        OrderReplaceRejected e => new OrderReplaceRejectedMessage(e.OrderId),
        _ => throw new ArgumentOutOfRangeException(nameof(orderEvent), orderEvent, "Unknown order event.")
    };

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
            OrderTopology.EventsExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
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
