using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Portfolio.Infrastructure.Telemetry;
using Moonad;
using TradeSimulator.Portfolio.Processors.PortfolioCommands;
using TradeSimulator.Portfolio.Infrastructure.Serialization;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

public sealed class RabbitMqResponsePublisher(IConfiguration configuration) : IResponsePublisher, IAsyncDisposable
{
    public const string ExchangeName = "trade-simulator.portfolio.events";
    public const string FailedRoutingKey = "asset.command.failed";

    private IConnection? _connection;
    private IChannel? _channel;
    private readonly HashSet<string> _declared = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task PublishFailureAsync(PortfolioCommandError failure, CancellationToken cancellationToken)
    {
        var response = new PortfolioResponse(
            Guid.CreateVersion7(), PortfolioEventTypes.CommandResult,
            Result<PortfolioCommandResult, PortfolioCommandError>.Error(failure));
        return PublishAsync(ExchangeName, FailedRoutingKey, JsonSerializer.Serialize(response, PortfolioJson.Options), cancellationToken);
    }

    public Task PublishPendingAsync(PendingResponse pending, CancellationToken cancellationToken) =>
        PublishAsync(pending.Exchange ?? ExchangeName, pending.RoutingKey, pending.Payload, cancellationToken, pending.TraceParent);

    private async Task PublishAsync(string exchange, string routingKey, string payload, CancellationToken cancellationToken, string? traceParent = null)
    {
        await _gate.WaitAsync(cancellationToken);
        using var activity = PortfolioTelemetry.Service.StartPublisher(exchange, routingKey, traceParent);
        try
        {
            if (_channel is null)
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://localhost:5672/")
                };
                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            }

            if (_declared.Add(exchange))
                await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);

            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
            if (activity?.Id is { } id)
                properties.Headers = new Dictionary<string, object?> { [ServiceTelemetry.TraceParentHeader] = id };
            await _channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(payload),
                cancellationToken: cancellationToken);
            PortfolioTelemetry.Service.OutboxPublished(exchange);
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            await ResetAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _gate.Dispose();
    }

    private async Task ResetAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
        _channel = null;
        _connection = null;
        _declared.Clear();
    }
}
