using TradeSimulator.Commons.Messaging;
using TradeSimulator.Outbox;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Portfolio.Common;
using MongoDB.Driver;
using TradeSimulator.Portfolio.Features.DepositAsset;
using TradeSimulator.Portfolio.Features.GetAsset;
using TradeSimulator.Portfolio.Infrastructure.ReadModel;
using TradeSimulator.Portfolio.Processors.ReleaseReservation;
using TradeSimulator.Portfolio.Features.ReserveAssets;
using TradeSimulator.Portfolio.Processors.SettleReservation;
using TradeSimulator.Portfolio.Features.SearchAssets;
using TradeSimulator.Portfolio.Infrastructure.Database;
using TradeSimulator.Portfolio.Infrastructure.Messaging;
using TradeSimulator.Portfolio.Infrastructure.Realtime;
using TradeSimulator.Portfolio.Infrastructure.Serialization;
using TradeSimulator.Portfolio.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Portfolio");
builder.Services.AddOpenApi();
builder.Services.AddGracefulShutdown();
builder.Services.AddTradeSimulatorTelemetry(builder.Configuration, PortfolioTelemetry.Service);
builder.Services.AddHealthChecks()
    .AddProbe("postgres", async (sp, ct) =>
    {
        await using var connection = await sp.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
    })
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
builder.Services.AddSingleton<PostgresAssetStore>();
builder.Services.AddSingleton<IAssetStore>(sp => sp.GetRequiredService<PostgresAssetStore>());
builder.Services.AddSingleton<IOutboxStore<PendingEvent>>(sp => sp.GetRequiredService<PostgresAssetStore>());
builder.Services.AddOutboxBacklogMonitor<PostgresAssetStore>();
MongoSetup.Initialize();
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(
    builder.Configuration.GetConnectionString("Mongo") ?? "mongodb://localhost:27017"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>()
    .GetDatabase(builder.Configuration["Mongo:Database"] ?? "portfolio"));
builder.Services.AddSingleton<MongoAssetReadModel>();
builder.Services.AddSingleton<IAssetSearch>(sp => sp.GetRequiredService<MongoAssetReadModel>());
builder.Services.AddSingleton<IAssetLookup>(sp => sp.GetRequiredService<MongoAssetReadModel>());
builder.Services.AddSingleton<AssetProjectionHandler>();
builder.Services.AddHostedService<AssetProjectionConsumer>();
builder.Services.AddSingleton<ISearchAssets, SearchAssetsHandler>();
builder.Services.AddSingleton<IGetAsset, GetAssetHandler>();
builder.Services.AddSingleton<IOutboxPublisher<PendingEvent>, RabbitMqEventPublisher>();
builder.Services.AddSingleton<IAssetNotifier, SignalRAssetNotifier>();
builder.Services.AddSingleton<IDepositAsset, DepositAssetHandler>();
builder.Services.AddSingleton<IReserveAssets, ReserveAssetsHandler>();
builder.Services.AddSingleton<ISettleReservation, SettleReservationHandler>();
builder.Services.AddSingleton<IReleaseReservation, ReleaseReservationHandler>();
builder.Services.AddSingleton<OrderEventHandler>();
builder.Services.AddHostedService<OrderEventConsumer>();
builder.Services.AddOutboxPublisher<PendingEvent>("portfolio events", TimeSpan.FromMilliseconds(200));

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

app.MapTradeSimulatorHealth();
app.MapSearchAssetsEndpoint();
app.MapGetAssetEndpoint();
app.MapDepositAssetEndpoint();
app.MapReserveAssetsEndpoint();
app.MapHub<PortfolioHub>(PortfolioHub.Route);
app.Run();

public partial class Program { }
