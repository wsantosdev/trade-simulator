using Microsoft.AspNetCore.Diagnostics;
using TradingSimulator.Account.Api.Features.AccountCreation;
using TradingSimulator.Account.Api.Features.AccountQuery;
using TradingSimulator.Account.Api.Features.Deposits;
using TradingSimulator.Account.Api.Features.Reservations;
using TradingSimulator.Account.Api.Features.Withdrawals;
using TradingSimulator.Account.Application;
using TradingSimulator.Account.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Accounts");
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton(sp =>
{
    return new PostgresAccountStore(
        connectionString ?? string.Empty,
        sp.GetRequiredService<ILogger<PostgresAccountStore>>());
});
builder.Services.AddSingleton<IAccountStore>(sp => sp.GetRequiredService<PostgresAccountStore>());
builder.Services.AddSingleton<IOutbox>(sp => sp.GetRequiredService<PostgresAccountStore>());
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddSingleton<IOrderFundsStore>(sp => sp.GetRequiredService<PostgresAccountStore>());
builder.Services.AddSingleton<IResponseOutbox>(sp => sp.GetRequiredService<PostgresAccountStore>());
builder.Services.AddHttpClient<IOfferBookClient, HttpOfferBookClient>(client =>
{
    client.BaseAddress = new Uri((builder.Configuration["OfferBook:BaseUrl"] ?? "http://localhost:5050").TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<OrderFundsProcessor>();
builder.Services.AddSingleton<IResponsePublisher, RabbitMqResponsePublisher>();
builder.Services.AddHostedService<ResponseOutboxPublisherService>();
builder.Services.AddHostedService<OrderSubmittedConsumer>();
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
    await next();
});
app.UseCors();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapOpenAccountEndpoint();
app.MapGetAccountEndpoint();
app.MapDepositEndpoint();
app.MapWithdrawEndpoint();
app.MapReserveFundsEndpoint();
app.Run();

public partial class Program { }
