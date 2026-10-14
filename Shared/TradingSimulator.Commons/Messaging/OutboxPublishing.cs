using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TradingSimulator.Commons.Messaging;

public static class OutboxPublishing
{
    // Upper bound to publish and mark a single outbox item, also applied while stopping.
    public static readonly TimeSpan ItemTimeout = TimeSpan.FromSeconds(10);

    // Must exceed the longest drain in the workers; the container needs a slightly longer stop_grace_period.
    public static IServiceCollection AddGracefulShutdown(this IServiceCollection services) =>
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
}
