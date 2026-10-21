using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Quotes.Common;
using TradeSimulator.Quotes.Features.GetLatestQuote;
using TradeSimulator.Quotes.Infrastructure.Messaging;
using TradeSimulator.Quotes.Processors.QuoteInterest;
using TradeSimulator.Quotes.Processors.QuoteUpdates;
using TradeSimulator.Quotes.Infrastructure.Realtime;
using TradeSimulator.Quotes.Infrastructure.Serialization;
using TradeSimulator.Quotes.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddTradeSimulatorTelemetry(builder.Configuration, QuotesTelemetry.Service);
builder.Services.AddHealthChecks().AddRabbitMqProbe();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    foreach (var converter in QuoteJson.Options.Converters)
        options.SerializerOptions.Converters.Add(converter);
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    foreach (var converter in QuoteJson.Options.Converters)
        options.PayloadSerializerOptions.Converters.Add(converter);
});
builder.Services.AddSingleton<LatestQuotes>();
builder.Services.AddSingleton<QuoteSubscriptions>();
builder.Services.AddSingleton<QuoteProcessor>();
builder.Services.AddSingleton<IGetLatestQuote, GetLatestQuoteHandler>();
builder.Services.AddSingleton<QuoteInterestHeartbeat>();
builder.Services.AddSingleton<IQuoteNotifier, SignalRQuoteNotifier>();
builder.Services.AddSingleton<IQuoteInterestPublisher, RabbitMqQuoteInterestPublisher>();
builder.Services.AddHostedService<RabbitMqQuoteConsumer>();
builder.Services.AddHostedService<QuoteInterestHeartbeatService>();

var app = builder.Build();
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
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGetLatestQuoteEndpoint();
app.MapHub<QuotesHub>(QuotesHub.Route);
app.Run();

public partial class Program;
