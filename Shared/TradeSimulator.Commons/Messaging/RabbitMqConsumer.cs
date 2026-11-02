using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Commons.Messaging;

// Base of every queue consumer: declares the topology, reconnects on failure and hands each body to HandleAsync.
// Reliable consumers acknowledge after handling and requeue on failure. Perishable ones (AutoAck) never retry.
public abstract class RabbitMqConsumer(IConfiguration configuration, ILogger logger) : BackgroundService
{
    protected abstract ServiceTelemetry Telemetry { get; }
    protected abstract string ExchangeName { get; }
    protected abstract string QueueName { get; }
    protected abstract IReadOnlyList<string> RoutingKeys { get; }
    protected virtual bool AutoAck => false;
    protected virtual IDictionary<string, object?>? QueueArguments => null;

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

    // Runs once per connection, before the first message (e.g. to prepare a database).
    protected virtual Task OnConnectingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await OnConnectingAsync(stoppingToken);

        var factory = new ConnectionFactory { Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://localhost:5672/") };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            QueueName, durable: true, exclusive: false, autoDelete: false, arguments: QueueArguments, cancellationToken: stoppingToken);
        foreach (var routingKey in RoutingKeys)
            await channel.QueueBindAsync(QueueName, ExchangeName, routingKey, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };

        using var drain = new ConsumerDrain();

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            using var inFlight = drain.Begin();
            using var activity = Telemetry.StartConsumer(QueueName, ServiceTelemetry.TraceParentOf(delivery.BasicProperties.Headers));
            try
            {
                await HandleAsync(Encoding.UTF8.GetString(delivery.Body.Span), drain.Token);
                if (!AutoAck)
                    await channel.BasicAckAsync(delivery.DeliveryTag, false, drain.Token);
                Telemetry.MessageConsumed(QueueName, AutoAck ? "processed" : "acked");
            }
            catch (OperationCanceledException) when (drain.TimedOut)
            {
            }
            catch (Exception exception)
            {
                activity.Fail(exception);
                if (AutoAck)
                {
                    Telemetry.MessageConsumed(QueueName, "failed");
                    logger.LogError(exception, "Failed to process a message from {Queue}; it is not retried.", QueueName);
                    return;
                }

                Telemetry.MessageConsumed(QueueName, "requeued");
                logger.LogError(exception, "Failed to process a message from {Queue}; it will be redelivered.", QueueName);
                await Task.Delay(TimeSpan.FromSeconds(2), drain.Token);
                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, drain.Token);
            }
        };
        var consumerTag = await channel.BasicConsumeAsync(QueueName, AutoAck, consumer, stoppingToken);
        logger.LogInformation("Consuming messages from {Queue}.", QueueName);

        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, stoppingToken));

        if (stoppingToken.IsCancellationRequested && channel.IsOpen)
            await drain.StopAsync(channel, consumerTag, QueueName, logger);
    }
}
