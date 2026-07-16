using ContentPlatform.SharedKernel;

namespace ContentPlatform.Publishing.Domain;

/// <summary>Tek bir yayın denemesi (teşhis + idempotency izi).</summary>
public sealed class DeliveryAttempt : Entity
{
    private DeliveryAttempt() { }
    public DeliveryAttempt(Guid publicationId, int attemptNo, DeliveryOutcome outcome, string? error, IClock clock)
    {
        PublicationId = publicationId;
        AttemptNo = attemptNo;
        Outcome = outcome;
        Error = error;
        CreatedAt = clock.UtcNow;
    }

    public Guid PublicationId { get; private set; }
    public int AttemptNo { get; private set; }
    public DeliveryOutcome Outcome { get; private set; }
    public string? Error { get; private set; }
}
