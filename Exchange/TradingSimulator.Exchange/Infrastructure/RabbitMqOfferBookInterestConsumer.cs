using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradingSimulator.Exchange.Application;

namespace TradingSimulator.Exchange.Infrastructure;

public sealed class RabbitMqOfferBookInterestConsumer(
    IConfiguration configuration,
    OfferBookInterestProcessor processor,
    ILogger<RabbitMqOfferBookInterestConsumer> logger) : BackgroundService
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
                logger.LogError(exception, "Offer book interest consumer failed; reconnecting.");
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
            OfferBookTopology.InterestExchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            OfferBookTopology.InterestQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-message-ttl"] = OfferBookTopology.InterestMessageTtlMilliseconds },
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(OfferBookTopology.InterestQueue, OfferBookTopology.InterestExchange, "#", cancellationToken: stoppingToken);

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var result = await processor.ProcessAsync(Encoding.UTF8.GetString(delivery.Body.Span), MessageJson.Options, stoppingToken);
                if (result.IsError)
                    logger.LogWarning("Dropped a malformed offer book interest message.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Interest is a heartbeat and snapshots are requested again on a gap, so a failure here is not retried.
                logger.LogError(exception, "Could not process an offer book interest message.");
            }
        };
        await channel.BasicConsumeAsync(OfferBookTopology.InterestQueue, autoAck: true, consumer, stoppingToken);
        logger.LogInformation("Consuming offer book interest from {Queue}.", OfferBookTopology.InterestQueue);

        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, stoppingToken));
    }
}
