using TradingSimulator.OfferBook.Api.Features.OfferBookQuery;
using TradingSimulator.OfferBook.Api.Hubs;
using TradingSimulator.OfferBook.Application;
using TradingSimulator.OfferBook.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
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
    await next();
});
app.UseCors();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapOfferBookQueryEndpoints();
app.MapHub<OfferBookHub>(OfferBookHub.Route);
app.Run();

public partial class Program;
