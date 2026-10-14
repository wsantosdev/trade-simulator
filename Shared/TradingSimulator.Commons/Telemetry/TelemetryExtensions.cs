using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

namespace TradingSimulator.Commons.Telemetry;

public static class TelemetryExtensions
{
    public const string ReadyTag = "ready";

    // The OTLP exporter is only enabled when OTEL_EXPORTER_OTLP_ENDPOINT is configured.
    public static IServiceCollection AddTradingSimulatorTelemetry(
        this IServiceCollection services, IConfiguration configuration, ServiceTelemetry telemetry)
    {
        services.AddSingleton(telemetry);
        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(telemetry.Name))
            .WithTracing(tracing => tracing
                .AddSource(telemetry.Name)
                .AddSource("Npgsql")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(telemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithLogging();
        if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            otel.UseOtlpExporter();
        return services;
    }

    public static IServiceCollection AddOutboxBacklogMonitor<TReader>(this IServiceCollection services)
        where TReader : class, IOutboxBacklogReader
    {
        services.AddSingleton<IOutboxBacklogReader>(sp => sp.GetRequiredService<TReader>());
        return services.AddHostedService<OutboxBacklogMonitor>();
    }

    public static IHealthChecksBuilder AddProbe(
        this IHealthChecksBuilder builder, string name, Func<IServiceProvider, CancellationToken, Task> probe) =>
        builder.Add(new HealthCheckRegistration(name, sp => new ProbeHealthCheck(name, ct => probe(sp, ct)), null, [ReadyTag]));

    public static IHealthChecksBuilder AddRabbitMqProbe(this IHealthChecksBuilder builder) =>
        builder.AddProbe("rabbitmq", async (sp, ct) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var factory = new ConnectionFactory { Uri = new Uri(configuration["RabbitMq:Uri"] ?? "amqp://localhost:5672/") };
            await using var connection = await factory.CreateConnectionAsync(ct);
        });

    public static IEndpointRouteBuilder MapTradingSimulatorHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new() { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains(ReadyTag) });
        return endpoints;
    }
}

internal sealed class ProbeHealthCheck(string name, Func<CancellationToken, Task> probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await probe(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"{name} is unreachable.", exception);
        }
    }
}
