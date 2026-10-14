using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradingSimulator.Account.Infrastructure.Telemetry;
using TradingSimulator.Commons.Telemetry;

namespace TradingSimulator.Account.Infrastructure.Messaging;

public abstract class RabbitMqConsumerBase(IConfiguration configuration, ILogger logger) : BackgroundService
{
    protected abstract string ExchangeName { get; }
    protected abstract string QueueName { get; }
    protected abstract IReadOnlyList<string> RoutingKeys { get; }

    // How long an in-flight message may keep running after a stop was requested.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(20);

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

        // Messages already received are processed with this token, not the stopping one, so a stop never
        // aborts a handler halfway; it is only cancelled if draining exceeds DrainTimeout.
        using var drain = new CancellationTokenSource();
        var inFlight = 0;

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            Interlocked.Increment(ref inFlight);
            using var activity = AccountTelemetry.Service.StartConsumer(QueueName, TraceParentOf(delivery));
            try
            {
                await HandleAsync(Encoding.UTF8.GetString(delivery.Body.Span), drain.Token);
                await channel.BasicAckAsync(delivery.DeliveryTag, false, drain.Token);
                AccountTelemetry.Service.MessageConsumed(QueueName, "acked");
            }
            catch (OperationCanceledException) when (drain.IsCancellationRequested)
            {
                // Not acknowledged: the broker redelivers it after the connection closes.
            }
            catch (Exception exception)
            {
                activity.Fail(exception);
                AccountTelemetry.Service.MessageConsumed(QueueName, "requeued");
                logger.LogError(exception, "Failed to process a message from {Queue}; it will be redelivered.", QueueName);
                await Task.Delay(TimeSpan.FromSeconds(2), drain.Token);
                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, drain.Token);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        };
        var consumerTag = await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consuming messages from {Queue}.", QueueName);

        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, stoppingToken));

        if (stoppingToken.IsCancellationRequested && channel.IsOpen)
            await DrainAsync(channel, consumerTag, () => Volatile.Read(ref inFlight), drain);
    }

    private async Task DrainAsync(IChannel channel, string consumerTag, Func<int> inFlight, CancellationTokenSource drain)
    {
        drain.CancelAfter(DrainTimeout);
        try
        {
            // Stop receiving new messages, then let the ones in progress finish.
            await channel.BasicCancelAsync(consumerTag, noWait: false, drain.Token);
            logger.LogInformation("Stopping {Queue}: waiting for {Count} message(s) in progress.", QueueName, inFlight());
            while (inFlight() > 0)
                await Task.Delay(50, drain.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Stopping {Queue}: gave up waiting; unfinished messages will be redelivered.", QueueName);
        }
    }

    private static string? TraceParentOf(BasicDeliverEventArgs delivery) =>
        ServiceTelemetry.TraceParentOf(delivery.BasicProperties.Headers);
}
