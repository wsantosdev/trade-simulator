using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TradeSimulator.Outbox;

public static class OutboxPublishing
{
    // Upper bound to publish and mark a single outbox item, also applied while stopping.
    public static readonly TimeSpan ItemTimeout = TimeSpan.FromSeconds(10);

    // Must exceed the longest drain in the workers; the container needs a slightly longer stop_grace_period.
    public static IServiceCollection AddGracefulShutdown(this IServiceCollection services) =>
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

    // Registers the background worker; the store and the publisher must be registered by the service.
    public static IServiceCollection AddOutboxPublisher<TItem>(this IServiceCollection services, string name, TimeSpan idleDelay)
        where TItem : IOutboxItem =>
        services
            .AddSingleton(new OutboxPublisherOptions(name, idleDelay))
            .AddHostedService<OutboxPublisherService<TItem>>();
}
