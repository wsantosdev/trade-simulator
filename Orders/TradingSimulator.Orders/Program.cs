using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using TradingSimulator.Orders.Api.Features.Cancellation;
using TradingSimulator.Orders.Api.Features.Queries;
using TradingSimulator.Orders.Api.Features.Replacement;
using TradingSimulator.Orders.Api.Features.Submission;
using TradingSimulator.Orders.Api.Hubs;
using TradingSimulator.Orders.Application;
using TradingSimulator.Orders.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Orders");
builder.Services.AddOpenApi();
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
builder.Services.AddSingleton<IOrderQueries, PostgresOrderQueries>();
builder.Services.AddSingleton<IOrderNotifier, SignalROrderNotifier>();
builder.Services.AddSingleton<OrderRequestService>();
builder.Services.AddSingleton<ICommandPublisher, RabbitMqCommandPublisher>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddSingleton<ExchangeEventProcessor>();
builder.Services.AddHostedService<RabbitMqExchangeEventConsumer>();
builder.Services.AddSingleton<SubmissionOutcomeHandler>();
builder.Services.AddSingleton<FundsReservationProcessor>();
builder.Services.AddSingleton<AssetReservationProcessor>();
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
    app.Logger.LogCritical("Order persistence could not be initialized: {Error}", initialization.ErrorValue.GetType().Name);
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
    await next();
});
app.UseCors();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapSendOrderEndpoint();
app.MapCancelOrderEndpoint();
app.MapReplaceOrderEndpoint();
app.MapOrderQueryEndpoints();
app.MapHub<OrdersHub>(OrdersHub.Route);
app.Run();

public partial class Program { }
