namespace ContentPlatform.Abstractions;

/// <summary>Bağlamlar arası olay tabanı (in-process; ileride mesaj kuyruğuna çıkarılabilir).</summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}
