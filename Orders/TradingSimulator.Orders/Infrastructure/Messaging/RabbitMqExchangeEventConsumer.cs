using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradingSimulator.Commons.Messaging;
using TradingSimulator.Commons.Telemetry;
using TradingSimulator.Orders.Infrastructure.Telemetry;

namespace TradingSimulator.Orders.Infrastructure.Messaging;

public sealed class RabbitMqExchangeEventConsumer(
    IConfiguration configuration,
    ExchangeEventMessageHandler handler,
    ILogger<RabbitMqExchangeEventConsumer> logger) : BackgroundService
{
    public const string ExchangeName = "trading-simulator.exchange.order.events";
    public const string QueueName = "trading-simulator.orders.order.events";

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
                logger.LogError(exception, "Exchange event consumer failed; reconnecting.");
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
        await channel.QueueBindAsync(QueueName, ExchangeName, "#", cancellationToken: stoppingToken);
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
            using var activity = OrdersTelemetry.Service.StartConsumer(QueueName, ServiceTelemetry.TraceParentOf(delivery.BasicProperties.Headers));
            try
            {
                var body = Encoding.UTF8.GetString(delivery.Body.Span);
                await handler.HandleAsync(body, drain.Token);
                await channel.BasicAckAsync(delivery.DeliveryTag, false, drain.Token);
                OrdersTelemetry.Service.MessageConsumed(QueueName, "acked");
            }
            catch (OperationCanceledException) when (drain.TimedOut)
            {
            }
            catch (Exception exception)
            {
                activity.Fail(exception);
                OrdersTelemetry.Service.MessageConsumed(QueueName, "requeued");
                logger.LogError(exception, "Failed to process an exchange event; it will be redelivered.");
                await Task.Delay(TimeSpan.FromSeconds(2), drain.Token);
                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, drain.Token);
            }
        };
        var consumerTag = await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consuming exchange events from {Queue}.", QueueName);

        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, stoppingToken));

        if (stoppingToken.IsCancellationRequested && channel.IsOpen)
            await drain.StopAsync(channel, consumerTag, QueueName, logger);
    }
}
