using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradingSimulator.Exchange.Common;
using TradingSimulator.Exchange.Infrastructure.Serialization;

namespace TradingSimulator.Exchange.Infrastructure.Messaging;

public sealed class RabbitMqQuoteInterestConsumer(
    IConfiguration configuration,
    QuoteInterestProcessor processor,
    ILogger<RabbitMqQuoteInterestConsumer> logger) : BackgroundService
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
                logger.LogError(exception, "Quote interest consumer failed; reconnecting.");
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
            QuoteTopology.InterestExchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            QuoteTopology.InterestQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-message-ttl"] = QuoteTopology.InterestMessageTtlMilliseconds },
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(QuoteTopology.InterestQueue, QuoteTopology.InterestExchange, "#", cancellationToken: stoppingToken);

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            var result = processor.Process(Encoding.UTF8.GetString(delivery.Body.Span), MessageJson.Options);
            if (result.IsError)
                logger.LogWarning("Dropped a malformed quote interest message.");
            return Task.CompletedTask;
        };
        // Interest is a heartbeat: a lost message is replaced by the next one, so no acknowledgement is needed.
        await channel.BasicConsumeAsync(QuoteTopology.InterestQueue, autoAck: true, consumer, stoppingToken);
        logger.LogInformation("Consuming quote interest from {Queue}.", QuoteTopology.InterestQueue);

        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, stoppingToken));
    }
}
