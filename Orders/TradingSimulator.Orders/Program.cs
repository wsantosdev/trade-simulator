using TradingSimulator.Commons.Messaging;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using TradingSimulator.Commons.Telemetry;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain.Entities;
using TradingSimulator.Orders.Features.CancelOrder;
using TradingSimulator.Orders.Features.QueryOrderHistory;
using TradingSimulator.Orders.Features.QueryOrders;
using TradingSimulator.Orders.Features.ReplaceOrder;
using TradingSimulator.Orders.Features.Shared;
using TradingSimulator.Orders.Features.SubmitOrder;
using TradingSimulator.Orders.Infrastructure.Database;
using TradingSimulator.Orders.Infrastructure.Messaging;
using TradingSimulator.Orders.Infrastructure.Realtime;
using TradingSimulator.Orders.Infrastructure.Serialization;
using TradingSimulator.Orders.Infrastructure.Telemetry;
using TradingSimulator.Orders.Processors.AssetReservation;
using TradingSimulator.Orders.Processors.OrderTransition;
using TradingSimulator.Orders.Processors.FundsReservation;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Orders");
builder.Services.AddOpenApi();
builder.Services.AddGracefulShutdown();
builder.Services.AddTradingSimulatorTelemetry(builder.Configuration, OrdersTelemetry.Service);
builder.Services.AddHealthChecks()
    .AddProbe("postgres", async (sp, ct) =>
    {
        await using var connection = await sp.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
    })
    .AddRabbitMqProbe();
builder.Services.AddSingleton<IOrdersMetrics, OrdersMetrics>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    foreach (var converter in OrderJson.Options.Converters)
        options.SerializerOptions.Converters.Add(converter);
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    foreach (var converter in OrderJson.Options.Converters)
        options.PayloadSerializerOptions.Converters.Add(converter);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString ?? string.Empty));
builder.Services.AddSingleton<PostgresOrderStore>();
builder.Services.AddSingleton<IOrderStore>(sp => sp.GetRequiredService<PostgresOrderStore>());
builder.Services.AddSingleton<IOutbox>(sp => sp.GetRequiredService<PostgresOrderStore>());
builder.Services.AddOutboxBacklogMonitor<PostgresOrderStore>();
builder.Services.AddSingleton<PostgresOrderSearch>();
builder.Services.AddSingleton<IOrderSearch>(sp => sp.GetRequiredService<PostgresOrderSearch>());
builder.Services.AddSingleton<IScheduledOrders>(sp => sp.GetRequiredService<PostgresOrderSearch>());
builder.Services.AddSingleton<IMarketHours>(sp => new MarketHours(
    sp.GetRequiredService<TimeProvider>(), builder.Configuration.GetValue<TimeSpan>("Simulation:SessionClockOffset")));
builder.Services.AddHostedService<ScheduledOrderDispatcher>();
builder.Services.AddSingleton<IOrderHistory, PostgresOrderHistory>();
builder.Services.AddSingleton<IOrderNotifier, SignalROrderNotifier>();
builder.Services.AddSingleton<OrderTransitions>();
builder.Services.AddSingleton<IOrderTransitions>(sp => sp.GetRequiredService<OrderTransitions>());
builder.Services.AddSingleton<OrderModifier>();
builder.Services.AddSingleton<ISubmitOrder, SubmitOrderHandler>();
builder.Services.AddSingleton<ICancelOrder, CancelOrderHandler>();
builder.Services.AddSingleton<IReplaceOrder, ReplaceOrderHandler>();
builder.Services.AddSingleton<IQueryOrders, QueryOrdersHandler>();
builder.Services.AddSingleton<IQueryOrderHistory, QueryOrderHistoryHandler>();
builder.Services.AddSingleton<ICommandPublisher, RabbitMqCommandPublisher>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddSingleton<OrderTransitionProcessor>();
builder.Services.AddSingleton<ExchangeEventMessageHandler>();
builder.Services.AddHostedService<RabbitMqExchangeEventConsumer>();
builder.Services.AddSingleton<FundsReservationProcessor>();
builder.Services.AddSingleton<FundsReservationMessageHandler>();
builder.Services.AddSingleton<AssetReservationProcessor>();
builder.Services.AddSingleton<AssetReservationMessageHandler>();
builder.Services.AddHostedService<RabbitMqFundsReservationConsumer>();
builder.Services.AddHostedService<RabbitMqAssetReservationConsumer>();

var app = builder.Build();
if (string.IsNullOrWhiteSpace(connectionString))
{
    app.Logger.LogCritical("ConnectionStrings:Orders is required.");
    return;
}

var orderStore = app.Services.GetRequiredService<PostgresOrderStore>();
var initialization = await orderStore.InitializeAsync(app.Lifetime.ApplicationStopping);
if (initialization.IsError)
{
    app.Logger.LogCritical("Order persistence could not be initialized: {Error}",
        initialization.ErrorValue.GetType().Name);
    return;
}

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
app.MapSendOrderEndpoint();
app.MapCancelOrderEndpoint();
app.MapReplaceOrderEndpoint();
app.MapSearchOrdersEndpoint();
app.MapQueryOrderHistoryEndpoint();
app.MapHub<OrdersHub>(OrdersHub.Route);
app.Run();

public partial class Program
{
}