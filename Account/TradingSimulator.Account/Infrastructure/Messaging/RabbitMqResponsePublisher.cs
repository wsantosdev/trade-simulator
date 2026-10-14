using System.Text;
using RabbitMQ.Client;
using TradingSimulator.Account.Common;

namespace TradingSimulator.Account.Infrastructure.Messaging;

public sealed class RabbitMqResponsePublisher(IConfiguration configuration) : IResponsePublisher, IAsyncDisposable
{
    public const string ExchangeName = "trading-simulator.account.reservations";

    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PublishAsync(PendingResponse pending, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
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
            await _channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: pending.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(pending.Payload),
                cancellationToken: cancellationToken);
        }
        catch
        {
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
