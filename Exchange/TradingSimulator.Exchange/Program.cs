using Npgsql;
using TradingSimulator.Exchange.Api;
using TradingSimulator.Exchange.Api.Features.InstrumentImport;
using TradingSimulator.Exchange.Api.Features.InstrumentQueries;
using TradingSimulator.Exchange.Api.Features.QuoteImport;
using TradingSimulator.Exchange.Api.Features.QuoteQueries;
using TradingSimulator.Exchange.Application;
using TradingSimulator.Exchange.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Exchange");
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4204").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString ?? string.Empty));
builder.Services.AddSingleton<PostgresInstrumentStore>();
builder.Services.AddSingleton<IInstrumentStore>(sp => sp.GetRequiredService<PostgresInstrumentStore>());
builder.Services.AddSingleton<IInstrumentQueries, PostgresInstrumentQueries>();
builder.Services.AddSingleton<IInstrumentSource, BvbgInstrumentSource>();
builder.Services.AddSingleton<ImportInstrumentsHandler>();
builder.Services.AddSingleton<IQuoteStore, PostgresQuoteStore>();
builder.Services.AddSingleton<IQuoteQueries, PostgresQuoteQueries>();
builder.Services.AddSingleton<IQuoteSource, CotahistQuoteSource>();
builder.Services.AddSingleton<ImportQuotesHandler>();
builder.Services.AddSingleton<IQuoteSimulationSource, PostgresQuoteSimulationSource>();
builder.Services.AddSingleton<IQuotePublisher, RabbitMqQuotePublisher>();
builder.Services.AddSingleton<QuoteInterestRegistry>();
builder.Services.AddSingleton<QuoteInterestProcessor>();
builder.Services.AddSingleton(sp => new QuoteSimulator(
    sp.GetRequiredService<IQuoteSimulationSource>(), sp.GetRequiredService<IQuotePublisher>(),
    sp.GetRequiredService<QuoteInterestRegistry>(), Random.Shared, sp.GetRequiredService<ILogger<QuoteSimulator>>()));
builder.Services.AddSingleton<IOrderEventPublisher, RabbitMqOrderEventPublisher>();
builder.Services.AddSingleton<IOfferBookPublisher, RabbitMqOfferBookPublisher>();
builder.Services.AddSingleton<OfferBookInterestRegistry>();
builder.Services.AddSingleton(_ => new SimulatedLiquidity(Random.Shared));
builder.Services.AddSingleton(sp => new OrderBookEngine(
    sp.GetRequiredService<QuoteSimulator>(), sp.GetRequiredService<IOrderEventPublisher>(),
    sp.GetRequiredService<IOfferBookPublisher>(), sp.GetRequiredService<OfferBookInterestRegistry>(),
    sp.GetRequiredService<SimulatedLiquidity>(), sp.GetRequiredService<TimeProvider>(),
    builder.Configuration.GetValue<TimeSpan>("Simulation:SessionClockOffset"),
    sp.GetRequiredService<ILogger<OrderBookEngine>>()));
builder.Services.AddSingleton<OrderCommandProcessor>();
builder.Services.AddSingleton<OfferBookInterestProcessor>();
builder.Services.AddHostedService<RabbitMqQuoteInterestConsumer>();
builder.Services.AddHostedService<RabbitMqOrderCommandConsumer>();
builder.Services.AddHostedService<RabbitMqOfferBookInterestConsumer>();
builder.Services.AddHostedService<QuoteSimulationService>();

var app = builder.Build();
if (string.IsNullOrWhiteSpace(connectionString))
{
    app.Logger.LogCritical("ConnectionStrings:Exchange is required.");
    return 1;
}

await app.Services.GetRequiredService<PostgresInstrumentStore>().InitializeAsync(app.Lifetime.ApplicationStopping);

var importPath = ArgumentValue(args, ImportInstrumentsCommand.Argument);
if (importPath is not null)
{
    if (importPath.Length == 0)
    {
        app.Logger.LogCritical("Usage: {Argument} <path to the BVBG.028 XML file>", ImportInstrumentsCommand.Argument);
        return 1;
    }

    return await ImportInstrumentsCommand.RunAsync(
        app.Services.GetRequiredService<ImportInstrumentsHandler>(), importPath, app.Logger, CancellationToken.None);
}

var quotesPath = ArgumentValue(args, ImportQuotesCommand.Argument);
if (quotesPath is not null)
{
    if (quotesPath.Length == 0)
    {
        app.Logger.LogCritical("Usage: {Argument} <path to the COTAHIST text file>", ImportQuotesCommand.Argument);
        return 1;
    }

    return await ImportQuotesCommand.RunAsync(
        app.Services.GetRequiredService<ImportQuotesHandler>(), quotesPath, app.Logger, CancellationToken.None);
}

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
app.MapInstrumentQueryEndpoints();
app.MapQuoteQueryEndpoints();
app.Run();
return 0;

static string? ArgumentValue(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index < 0 ? null : index + 1 < args.Length ? args[index + 1] : string.Empty;
}

public partial class Program;
