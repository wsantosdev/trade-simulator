using TradeSimulator.Commons.Messaging;
using TradeSimulator.Outbox;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using StackExchange.Redis;
using TradeSimulator.Account.Features.SubmitReservation;
using TradeSimulator.Account.Infrastructure.Redis;
using TradeSimulator.Account.Common;
using TradeSimulator.Account.Features.Shared;
using TradeSimulator.Account.Processors.OrderFunds;
using TradeSimulator.Account.Domain.Entities;
using TradeSimulator.Account.Features.Deposit;
using TradeSimulator.Account.Features.GetAccount;
using TradeSimulator.Account.Features.Stream;
using TradeSimulator.Account.Features.OpenAccount;
using TradeSimulator.Account.Features.ReserveFunds;
using TradeSimulator.Account.Features.WithdrawFunds;
using TradeSimulator.Account.Infrastructure.Database;
using TradeSimulator.Account.Infrastructure.Http;
using TradeSimulator.Account.Infrastructure.Messaging;
using TradeSimulator.Account.Infrastructure.Telemetry;
using TradeSimulator.Commons.Telemetry;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Accounts");
var redisConnection = builder.Configuration.GetConnectionString("Redis");
builder.Services.AddOpenApi();
builder.Services.AddGracefulShutdown();
builder.Services.AddTradeSimulatorTelemetry(builder.Configuration, AccountTelemetry.Service);
builder.Services.AddHealthChecks()
    .AddProbe("postgres", (sp, ct) => sp.GetRequiredService<PostgresAccountStore>().PingAsync(ct))
    .AddProbe("redis", async (sp, _) => await sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase().PingAsync())
    .AddRabbitMqProbe();
builder.Services.AddOutboxBacklogMonitor<PostgresAccountStore>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(new ConfigurationOptions
    {
        EndPoints = { redisConnection ?? "localhost:6379" },
        AbortOnConnectFail = false
    }));
builder.Services.AddSingleton<IFundsGate, RedisFundsGate>();
builder.Services.AddSingleton(sp =>
{
    return new PostgresAccountStore(
        connectionString ?? string.Empty,
        sp.GetRequiredService<ILogger<PostgresAccountStore>>(),
        sp.GetRequiredService<IFundsGate>());
});
builder.Services.AddSingleton<IAccountStore>(sp =>
    new InvalidatingAccountStore(sp.GetRequiredService<PostgresAccountStore>(), sp.GetRequiredService<IFundsGate>()));
builder.Services.AddSingleton<TradeSimulator.Account.Features.Stream.IAccountStream>(sp => sp.GetRequiredService<PostgresAccountStore>());
builder.Services.AddSingleton<IAccountMetrics, AccountMetrics>();
builder.Services.AddSingleton<IDeposit, DepositHandler>();
builder.Services.AddSingleton<IWithdrawFunds, WithdrawFundsHandler>();
builder.Services.AddSingleton<IReserveFunds, ReserveFundsHandler>();
builder.Services.AddSingleton<IGetAccount, GetAccountHandler>();
builder.Services.AddSingleton<IOpenAccount, OpenAccountHandler>();
builder.Services.AddSingleton<IOrderFundsStore>(sp => sp.GetRequiredService<PostgresAccountStore>());
builder.Services.AddSingleton<IOutboxStore<PendingResponse>>(sp => sp.GetRequiredService<PostgresAccountStore>());
builder.Services.AddHttpClient<IOfferBookClient, HttpOfferBookClient>(client =>
{
    client.BaseAddress = new Uri((builder.Configuration["OfferBook:BaseUrl"] ?? "http://localhost:5050").TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<OrderFundsProcessor>();
builder.Services.AddSingleton<IOutboxPublisher<PendingResponse>, RabbitMqResponsePublisher>();
builder.Services.AddOutboxPublisher<PendingResponse>("account responses", TimeSpan.FromSeconds(1));
builder.Services.AddHostedService<OrderScheduleCancelledConsumer>();
builder.Services.AddHostedService<ExchangeEventConsumer>();

var app = builder.Build();
if (string.IsNullOrWhiteSpace(connectionString))
{
    app.Logger.LogCritical("ConnectionStrings:Accounts is required.");
    return;
}

var accountStore = app.Services.GetRequiredService<PostgresAccountStore>();
var initialization = await accountStore.InitializeAsync(app.Lifetime.ApplicationStopping);
if (initialization.IsError)
{
    app.Logger.LogCritical("Account persistence could not be initialized: {Error}", initialization.ErrorValue.GetType().Name);
    return;
}

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(exception, "Unhandled infrastructure or framework exception.");
    const int status = StatusCodes.Status500InternalServerError;
    const string title = "Unexpected error";
    const string detail = "The request could not be processed.";
    context.Response.StatusCode = status;
    await Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(context);
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
app.MapOpenAccountEndpoint();
app.MapGetAccountEndpoint();
app.MapAccountStreamEndpoints();
app.MapDepositEndpoint();
app.MapWithdrawEndpoint();
app.MapReserveFundsEndpoint();
app.MapOrderReservationEndpoints();
app.Run();

public partial class Program { }
