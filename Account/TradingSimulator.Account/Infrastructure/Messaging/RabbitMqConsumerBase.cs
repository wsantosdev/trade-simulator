using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradingSimulator.Account.Infrastructure.Telemetry;

namespace TradingSimulator.Account.Infrastructure.Messaging;

public abstract class RabbitMqConsumerBase(IConfiguration configuration, ILogger logger) : BackgroundService
{
    protected abstract string ExchangeName { get; }
    protected abstract string QueueName { get; }
    protected abstract IReadOnlyList<string> RoutingKeys { get; }

    protected abstract Task HandleAsync(string body, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Consumer of {Queue} failed; reconnecting.", QueueName);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://localhost:5672/")
        };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        foreach (var routingKey in RoutingKeys)
            await channel.QueueBindAsync(QueueName, ExchangeName, routingKey, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            using var activity = AccountTelemetry.StartConsumer(QueueName, TraceParentOf(delivery));
            try
            {
                await HandleAsync(Encoding.UTF8.GetString(delivery.Body.Span), stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, false, stoppingToken);
                AccountTelemetry.MessageConsumed(QueueName, "acked");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                activity.Fail(exception);
                AccountTelemetry.MessageConsumed(QueueName, "requeued");
                logger.LogError(exception, "Failed to process a message from {Queue}; it will be redelivered.", QueueName);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, stoppingToken);
            }
        };
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consuming messages from {Queue}.", QueueName);

        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, stoppingToken));
    }

    private static string? TraceParentOf(BasicDeliverEventArgs delivery) =>
        delivery.BasicProperties.Headers?.TryGetValue(AccountTelemetry.TraceParentHeader, out var value) == true && value is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;
}
