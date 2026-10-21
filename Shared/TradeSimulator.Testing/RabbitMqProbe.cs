using System.Collections.Concurrent;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace TradeSimulator.Testing;

public sealed record ReceivedMessage(string RoutingKey, string Body, IDictionary<string, object?>? Headers);

// Binds a temporary queue to an exchange so a test can observe what a service publishes, and publishes into one.
public sealed class RabbitMqProbe : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly BlockingCollectionSlim _messages = new();

    private RabbitMqProbe(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitMqProbe> ConnectAsync(string uri)
    {
        var connection = await new ConnectionFactory { Uri = new Uri(uri) }.CreateConnectionAsync();
        return new RabbitMqProbe(connection, await connection.CreateChannelAsync());
    }

    public async Task ListenAsync(string exchange, string routingKey = "#")
    {
        await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
        var queue = (await _channel.QueueDeclareAsync(exclusive: true, autoDelete: true)).QueueName;
        await _channel.QueueBindAsync(queue, exchange, routingKey);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            _messages.Add(new ReceivedMessage(
                delivery.RoutingKey, Encoding.UTF8.GetString(delivery.Body.Span), delivery.BasicProperties.Headers));
            return Task.CompletedTask;
        };
        await _channel.BasicConsumeAsync(queue, autoAck: true, consumer);
    }

    // Consumers declare their queues on startup; publishing earlier would be dropped by the exchange.
    public async Task WaitForQueueAsync(string queue, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            try
            {
                await using var probe = await _connection.CreateChannelAsync();
                await probe.QueueDeclarePassiveAsync(queue);
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
    }

    public async Task PublishAsync(string exchange, string routingKey, string body)
    {
        await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
        await _channel.BasicPublishAsync(
            exchange, routingKey, mandatory: false,
            new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent },
            Encoding.UTF8.GetBytes(body));
    }

    public Task<ReceivedMessage> NextAsync(Func<ReceivedMessage, bool>? match = null, TimeSpan? timeout = null) =>
        _messages.NextAsync(match ?? (_ => true), timeout ?? TimeSpan.FromSeconds(20));

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class BlockingCollectionSlim
    {
        private readonly ConcurrentQueue<ReceivedMessage> _queue = new();

        public void Add(ReceivedMessage message) => _queue.Enqueue(message);

        public async Task<ReceivedMessage> NextAsync(Func<ReceivedMessage, bool> match, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            var skipped = new List<ReceivedMessage>();
            while (true)
            {
                while (_queue.TryDequeue(out var message))
                {
                    if (match(message))
                        return message;
                    skipped.Add(message);
                }

                if (cts.IsCancellationRequested)
                    throw new TimeoutException(
                        $"No matching message within {timeout.TotalSeconds:0}s. Seen: {string.Join(", ", skipped.Select(m => m.RoutingKey))}");
                await Task.Delay(50);
            }
        }
    }
}
