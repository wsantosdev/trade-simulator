using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradingSimulator.OfferBook.Common;
using TradingSimulator.OfferBook.Infrastructure.Serialization;

namespace TradingSimulator.OfferBook.Infrastructure.Messaging;

public sealed class RabbitMqOfferBookConsumer(
    IConfiguration configuration,
    OfferBookProcessor processor,
    ILogger<RabbitMqOfferBookConsumer> logger) : BackgroundService
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
                logger.LogError(exception, "Offer book consumer failed; reconnecting.");
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
            OfferBookTopology.BookEventsExchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            OfferBookTopology.BookQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = OfferBookTopology.BookMessageTtlMilliseconds,
                ["x-max-length"] = OfferBookTopology.BookMaxLength
            },
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(OfferBookTopology.BookQueue, OfferBookTopology.BookEventsExchange, "offerbook.*", cancellationToken: stoppingToken);

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
                var result = await processor.ProcessAsync(
                    Encoding.UTF8.GetString(delivery.Body.Span), OfferBookJson.Options, stoppingToken);
                if (result.IsError)
                    logger.LogWarning("Dropped a malformed offer book message.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Offer book message could not be processed.");
            }
        };
        // OfferBook are perishable and replaced every second, so no acknowledgement is needed.
        await channel.BasicConsumeAsync(OfferBookTopology.BookQueue, autoAck: true, consumer, stoppingToken);
        logger.LogInformation("Consuming offer books from {Queue}.", OfferBookTopology.BookQueue);

        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, stoppingToken));
    }
}
