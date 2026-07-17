using ContentPlatform.SharedKernel;

namespace ContentPlatform.Editorial.Domain;

/// <summary>
/// İçerik başına tek zaman çizelgesi (00 §19): kim/ne zaman/ne yaptı.
/// Değişmez kayıt — oluşturulduktan sonra düzenlenmez.
/// </summary>
public sealed class ContentAuditEntry : Entity
{
    private ContentAuditEntry() { } // EF

    public ContentAuditEntry(Guid contentItemId, AuditEvent @event, ActorType actorType, string actorRef, string? detail, IClock clock)
    {
        ContentItemId = contentItemId;
        Event = @event;
        ActorType = actorType;
        ActorRef = actorRef;
        Detail = detail;
        CreatedAt = clock.UtcNow;
    }

    public Guid ContentItemId { get; private set; }
    public AuditEvent Event { get; private set; }
    public ActorType ActorType { get; private set; }
    public string ActorRef { get; private set; } = default!;
    public string? Detail { get; private set; }
}
