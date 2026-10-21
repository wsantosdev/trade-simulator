using Microsoft.AspNetCore.Mvc.Testing;

namespace TradeSimulator.Testing;

// Runs a whole module (HTTP, consumers, outbox publishers) against real infrastructure.
public sealed class ModuleFactory<TProgram>(IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
    }
}
