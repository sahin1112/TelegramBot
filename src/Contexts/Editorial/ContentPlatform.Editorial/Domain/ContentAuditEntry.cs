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
        // Kolon sınırlarına (ActorRef 1000, Detail 1000) savunmacı kırpma: uzun kaynak URL'leri
        // audit INSERT'ini patlatmasın ("String or binary data would be truncated" düzeltmesi).
        ActorRef = actorRef.Length <= 1000 ? actorRef : actorRef[..1000];
        Detail = detail is { Length: > 1000 } ? detail[..1000] : detail;
        CreatedAt = clock.UtcNow;
    }

    public Guid ContentItemId { get; private set; }
    public AuditEvent Event { get; private set; }
    public ActorType ActorType { get; private set; }
    public string ActorRef { get; private set; } = default!;
    public string? Detail { get; private set; }
}
