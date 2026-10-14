using TradingSimulator.Commons.Messaging;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using StackExchange.Redis;
using TradingSimulator.Commons.Telemetry;
using TradingSimulator.Portfolio.Common;
using TradingSimulator.Portfolio.Features.GetAsset;
using TradingSimulator.Portfolio.Features.SearchAssets;
using TradingSimulator.Portfolio.Infrastructure.Database;
using TradingSimulator.Portfolio.Infrastructure.Idempotency;
using TradingSimulator.Portfolio.Infrastructure.Messaging;
using TradingSimulator.Portfolio.Infrastructure.Realtime;
using TradingSimulator.Portfolio.Infrastructure.Serialization;
using TradingSimulator.Portfolio.Infrastructure.Telemetry;
using TradingSimulator.Portfolio.Processors.PortfolioCommands;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Portfolio");
var redisConnection = builder.Configuration.GetConnectionString("Redis");
builder.Services.AddOpenApi();
builder.Services.AddGracefulShutdown();
builder.Services.AddTradingSimulatorTelemetry(builder.Configuration, PortfolioTelemetry.Service);
builder.Services.AddHealthChecks()
    .AddProbe("postgres", async (sp, ct) =>
    {
        await using var connection = await sp.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
    })
    .AddProbe("redis", async (sp, _) => await sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase().PingAsync())
    .AddRabbitMqProbe();
builder.Services.AddSingleton<IPortfolioMetrics, PortfolioMetrics>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    foreach (var converter in PortfolioJson.Options.Converters)
        options.SerializerOptions.Converters.Add(converter);
});
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    foreach (var converter in PortfolioJson.Options.Converters)
        options.PayloadSerializerOptions.Converters.Add(converter);
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString ?? string.Empty));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(new ConfigurationOptions
    {
        EndPoints = { redisConnection ?? "localhost:6379" },
        AbortOnConnectFail = false
    }));
builder.Services.AddSingleton<PostgresAssetStore>();
builder.Services.AddSingleton<IAssetStore>(sp => sp.GetRequiredService<PostgresAssetStore>());
builder.Services.AddSingleton<IOutbox>(sp => sp.GetRequiredService<PostgresAssetStore>());
builder.Services.AddOutboxBacklogMonitor<PostgresAssetStore>();
builder.Services.AddSingleton<PostgresAssetQueries>();
builder.Services.AddSingleton<IAssetSearch>(sp => sp.GetRequiredService<PostgresAssetQueries>());
builder.Services.AddSingleton<IAssetLookup>(sp => sp.GetRequiredService<PostgresAssetQueries>());
builder.Services.AddSingleton<ISearchAssets, SearchAssetsHandler>();
builder.Services.AddSingleton<IGetAsset, GetAssetHandler>();
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
builder.Services.AddSingleton<IResponsePublisher, RabbitMqResponsePublisher>();
builder.Services.AddSingleton<IAssetNotifier, SignalRAssetNotifier>();
builder.Services.AddSingleton<PortfolioCommandProcessor>();
builder.Services.AddSingleton<PortfolioCommandMessageHandler>();
builder.Services.AddSingleton<OrderEventMessageHandler>();
builder.Services.AddSingleton<ExchangeEventMessageHandler>();
builder.Services.AddHostedService<ResponseOutboxPublisherService>();
builder.Services.AddHostedService<PortfolioCommandConsumer>();
builder.Services.AddHostedService<OrderEventConsumer>();
builder.Services.AddHostedService<ExchangeEventConsumer>();

var app = builder.Build();
if (string.IsNullOrWhiteSpace(connectionString))
{
    app.Logger.LogCritical("ConnectionStrings:Portfolio is required.");
    return;
}

await app.Services.GetRequiredService<PostgresAssetStore>().InitializeAsync(app.Lifetime.ApplicationStopping);

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (exception is BadHttpRequestException badRequest)
    {
        app.Logger.LogWarning(badRequest, "Malformed request.");
        const int badStatus = StatusCodes.Status400BadRequest;
        context.Response.StatusCode = badStatus;
        await Results.Problem(statusCode: badStatus, title: "Invalid request", detail: "The request is malformed.")
            .ExecuteAsync(context);
        return;
    }

    app.Logger.LogError(exception, "Unhandled infrastructure or framework exception.");
    const int status = StatusCodes.Status500InternalServerError;
    context.Response.StatusCode = status;
    await Results.Problem(statusCode: status, title: "Unexpected error", detail: "The request could not be processed.")
        .ExecuteAsync(context);
}));

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId))
        correlationId = Guid.CreateVersion7().ToString("D");
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    System.Diagnostics.Activity.Current?.SetTag("correlation.id", correlationId);
    await next();
});
app.UseCors();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapTradingSimulatorHealth();
app.MapSearchAssetsEndpoint();
app.MapGetAssetEndpoint();
app.MapHub<PortfolioHub>(PortfolioHub.Route);
app.Run();

public partial class Program { }
