using System.Text;
using RabbitMQ.Client;
using TradeSimulator.Account.Infrastructure.Telemetry;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Account.Infrastructure.Messaging;

public sealed class RabbitMqResponsePublisher(IConfiguration configuration) : IResponsePublisher, IAsyncDisposable
{
    public const string ExchangeName = "trade-simulator.account.reservations";

    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PublishAsync(PendingResponse pending, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        using var activity = AccountTelemetry.Service.StartPublisher(ExchangeName, pending.RoutingKey, pending.TraceParent);
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
                routingKey: pending.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(pending.Payload),
                cancellationToken: cancellationToken);
            AccountTelemetry.Service.OutboxPublished(ExchangeName);
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
