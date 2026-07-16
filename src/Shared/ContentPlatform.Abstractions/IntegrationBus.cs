using Microsoft.Extensions.DependencyInjection;

namespace ContentPlatform.Abstractions;

/// <summary>Bağlamlar arası olay yayınlar (in-process; ileride mesaj kuyruğuna çıkarılabilir).</summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : IIntegrationEvent;
}

/// <summary>Kayıtlı IIntegrationEventHandler'ları çözüp sırayla çağırır.</summary>
internal sealed class InMemoryIntegrationEventPublisher(IServiceProvider serviceProvider) : IIntegrationEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : IIntegrationEvent
    {
        var handlers = serviceProvider.GetServices<IIntegrationEventHandler<TEvent>>();
        foreach (var handler in handlers)
            await handler.HandleAsync(@event, ct);
    }
}

public static class IntegrationBusServiceCollectionExtensions
{
    public static IServiceCollection AddIntegrationEventBus(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventPublisher, InMemoryIntegrationEventPublisher>();
        return services;
    }
}
