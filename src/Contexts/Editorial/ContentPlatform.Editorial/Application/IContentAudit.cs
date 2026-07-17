using ContentPlatform.Editorial.Domain;

namespace ContentPlatform.Editorial.Application;

/// <summary>
/// İçerik olay kaydı (izlenebilirlik). Log yalnız kaydı ekler; aynı birim-of-work içinde
/// çağıranın SaveChanges'i ile kalıcılaşır (EditorialDbContext paylaşılır).
/// </summary>
public interface IContentAudit
{
    void Log(Guid contentItemId, AuditEvent @event, ActorType actorType, string actorRef, string? detail = null);
    Task<IReadOnlyList<ContentAuditEntry>> GetTimelineAsync(Guid contentItemId, CancellationToken ct);
}
