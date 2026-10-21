using TradeSimulator.Commons.Telemetry;
using TradeSimulator.OfferBook.Common;
using TradeSimulator.OfferBook.Features.GetOfferBook;
using TradeSimulator.OfferBook.Infrastructure.Messaging;
using TradeSimulator.OfferBook.Infrastructure.Realtime;
using TradeSimulator.OfferBook.Processors.OfferBookInterest;
using TradeSimulator.OfferBook.Processors.OfferBookUpdates;
using TradeSimulator.OfferBook.Infrastructure.Serialization;
using TradeSimulator.OfferBook.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddTradeSimulatorTelemetry(builder.Configuration, OfferBookTelemetry.Service);
builder.Services.AddHealthChecks().AddRabbitMqProbe();
builder.Services.AddSingleton<IOfferBookMetrics, OfferBookMetrics>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    foreach (var converter in OfferBookJson.Options.Converters)
        options.SerializerOptions.Converters.Add(converter);
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    foreach (var converter in OfferBookJson.Options.Converters)
        options.PayloadSerializerOptions.Converters.Add(converter);
});
builder.Services.AddSingleton<LatestOfferBooks>();
builder.Services.AddSingleton<OfferBookSubscriptions>();
builder.Services.AddSingleton<OfferBookProcessor>();
builder.Services.AddSingleton<IGetOfferBook, GetOfferBookHandler>();
builder.Services.AddSingleton<OfferBookInterestHeartbeat>();
builder.Services.AddSingleton<IOfferBookNotifier, SignalROfferBookNotifier>();
builder.Services.AddSingleton<RabbitMqOfferBookInterestPublisher>();
builder.Services.AddSingleton<IOfferBookInterestPublisher>(sp => sp.GetRequiredService<RabbitMqOfferBookInterestPublisher>());
builder.Services.AddSingleton<IOfferBookSnapshotRequestPublisher>(sp => sp.GetRequiredService<RabbitMqOfferBookInterestPublisher>());
builder.Services.AddHostedService<RabbitMqOfferBookConsumer>();
builder.Services.AddHostedService<OfferBookInterestHeartbeatService>();

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
app.MapGetOfferBookEndpoint();
app.MapHub<OfferBookHub>(OfferBookHub.Route);
app.Run();

public partial class Program;
