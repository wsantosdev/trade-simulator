using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradingSimulator.Quotes.Common;
using TradingSimulator.Quotes.Infrastructure.Serialization;

namespace TradingSimulator.Quotes.Infrastructure.Messaging;

public sealed class RabbitMqQuoteConsumer(
    IConfiguration configuration,
    QuoteProcessor processor,
    ILogger<RabbitMqQuoteConsumer> logger) : BackgroundService
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
                logger.LogError(exception, "Quote consumer failed; reconnecting.");
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
            QuoteTopology.QuoteEventsExchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            QuoteTopology.QuoteQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = QuoteTopology.QuoteMessageTtlMilliseconds,
                ["x-max-length"] = QuoteTopology.QuoteMaxLength
            },
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(QuoteTopology.QuoteQueue, QuoteTopology.QuoteEventsExchange, "quote.*", cancellationToken: stoppingToken);

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
                    Encoding.UTF8.GetString(delivery.Body.Span), QuoteJson.Options, stoppingToken);
                if (result.IsError)
                    logger.LogWarning("Dropped a malformed quote message.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Quote message could not be processed.");
            }
        };
        // Quotes are perishable and replaced every second, so no acknowledgement is needed.
        await channel.BasicConsumeAsync(QuoteTopology.QuoteQueue, autoAck: true, consumer, stoppingToken);
        logger.LogInformation("Consuming quotes from {Queue}.", QuoteTopology.QuoteQueue);

        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, stoppingToken));
    }
}
