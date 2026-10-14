using TradingSimulator.Quotes.Common;
using TradingSimulator.Quotes.Features.QueryQuotes;
using TradingSimulator.Quotes.Infrastructure.Messaging;
using TradingSimulator.Quotes.Infrastructure.Realtime;
using TradingSimulator.Quotes.Infrastructure.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
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
    await next();
});
app.UseCors();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapQuoteQueryEndpoints();
app.MapHub<QuotesHub>(QuotesHub.Route);
app.Run();

public partial class Program;
