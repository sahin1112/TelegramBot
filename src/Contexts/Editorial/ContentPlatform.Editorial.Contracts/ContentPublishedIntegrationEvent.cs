using ContentPlatform.Abstractions;

namespace ContentPlatform.Editorial.Contracts;

/// <summary>İçerik en az bir hedefe yayınlandı → Editorial bunu Published olarak işaretler.</summary>
public sealed record ContentPublishedIntegrationEvent(
    Guid EventId, DateTimeOffset OccurredAt, Guid ContentItemId) : IIntegrationEvent;
