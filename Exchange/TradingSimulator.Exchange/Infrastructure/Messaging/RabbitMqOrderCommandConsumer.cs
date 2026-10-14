using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradingSimulator.Commons.Telemetry;
using TradingSimulator.Exchange.Infrastructure.Telemetry;
using TradingSimulator.Exchange.Infrastructure.Serialization;
using TradingSimulator.Exchange.Processors.OrderCommands;

namespace TradingSimulator.Exchange.Infrastructure.Messaging;

public sealed class RabbitMqOrderCommandConsumer(
    IConfiguration configuration,
    OrderBookEngine engine,
    ILogger<RabbitMqOrderCommandConsumer> logger) : BackgroundService
{
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
                logger.LogError(exception, "Order command consumer failed; reconnecting.");
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
        await channel.ExchangeDeclareAsync(
            OrderTopology.CommandsExchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            OrderTopology.CommandsQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        foreach (var routingKey in OrderTopology.CommandRoutingKeys)
            await channel.QueueBindAsync(OrderTopology.CommandsQueue, OrderTopology.CommandsExchange, routingKey, cancellationToken: stoppingToken);

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            using var activity = ExchangeTelemetry.Service.StartConsumer(
                OrderTopology.CommandsQueue, ServiceTelemetry.TraceParentOf(delivery.BasicProperties.Headers));
            try
            {
                var command = OrderCommandParser.Parse(Encoding.UTF8.GetString(delivery.Body.Span), MessageJson.Options);
                if (command.IsError)
                    logger.LogWarning("Dropped a malformed order command ({RoutingKey}).", delivery.RoutingKey);
                else
                    await engine.HandleAsync(command.ResultValue, stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                ExchangeTelemetry.Service.MessageConsumed(OrderTopology.CommandsQueue, "acked");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                activity.Fail(exception);
                ExchangeTelemetry.Service.MessageConsumed(OrderTopology.CommandsQueue, "requeued");
                logger.LogError(exception, "Could not process an order command; it will be redelivered.");
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
            }
        };
        await channel.BasicConsumeAsync(OrderTopology.CommandsQueue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consuming order commands from {Queue}.", OrderTopology.CommandsQueue);

        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, stoppingToken));
    }
}
