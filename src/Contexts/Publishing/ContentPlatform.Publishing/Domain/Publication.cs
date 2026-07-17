using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Publishing.Domain;

/// <summary>
/// Bir içeriğin bir hedefe yayını. Idempotency: (ContentItemId, Channel, TargetRef) UNIQUE.
/// Payload anlık kopyası (PayloadJson) saklanır → retry AI'yı tekrar çalıştırmaz.
/// </summary>
public sealed class Publication : Entity
{
    private readonly List<DeliveryAttempt> _attempts = new();
    private Publication() { }

    public Publication(Guid contentItemId, Channel channel, Guid socialAccountId, string targetRef, string payloadJson,
        Guid? categoryId, DateTimeOffset? scheduledAt, IClock clock)
    {
        ContentItemId = contentItemId;
        Channel = channel;
        SocialAccountId = socialAccountId;
        TargetRef = targetRef;
        PayloadJson = payloadJson;
        CategoryId = categoryId;
        ScheduledAt = scheduledAt;
        // Gelecek bir zamana planlandıysa "Scheduled"; değilse hemen gönderilebilir ("Pending").
        Status = scheduledAt is { } at && at > clock.UtcNow ? PublicationStatus.Scheduled : PublicationStatus.Pending;
        CreatedAt = clock.UtcNow;
    }

    public Guid ContentItemId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Channel Channel { get; private set; }
    public Guid SocialAccountId { get; private set; }
    public string TargetRef { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public PublicationStatus Status { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public string? ExternalId { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public IReadOnlyList<DeliveryAttempt> DeliveryAttempts => _attempts;

    public void MarkPublished(string? externalId, IClock clock)
    {
        Attempts++;
        Status = PublicationStatus.Published;
        ExternalId = externalId;
        Error = null;
        PublishedAt = clock.UtcNow;
        _attempts.Add(new DeliveryAttempt(Id, Attempts, DeliveryOutcome.Accepted, null, clock));
        Touch(clock);
    }

    public void MarkFailed(string? error, IClock clock)
    {
        Attempts++;
        Status = PublicationStatus.Failed;
        Error = error;
        _attempts.Add(new DeliveryAttempt(Id, Attempts, DeliveryOutcome.Failed, error, clock));
        Touch(clock);
    }

    /// <summary>Yayın zamanını değiştir. Geçmiş/boş → hemen ("Pending"); gelecek → "Scheduled". Yayınlanmışsa dokunma.</summary>
    public void Reschedule(DateTimeOffset? at, IClock clock)
    {
        if (Status == PublicationStatus.Published) return;
        ScheduledAt = at;
        Status = at is { } t && t > clock.UtcNow ? PublicationStatus.Scheduled : PublicationStatus.Pending;
        Touch(clock);
    }

    /// <summary>
    /// Kill-switch yüzünden gönderilmedi: Failed sayılmaz, "Scheduled/hemen due" olarak bekletilir;
    /// fren kalkınca ScheduledDispatchJob yeniden dener. Deneme sayacı artmaz.
    /// </summary>
    public void HoldForKillSwitch(IClock clock)
    {
        if (Status == PublicationStatus.Published) return;
        Status = PublicationStatus.Scheduled;
        ScheduledAt = clock.UtcNow;
        Touch(clock);
    }

    /// <summary>Planlı/başarısız yayını iptal et (bir daha denenmez).</summary>
    public void Cancel(IClock clock)
    {
        if (Status == PublicationStatus.Published) return;
        Status = PublicationStatus.Skipped;
        Touch(clock);
    }
}
