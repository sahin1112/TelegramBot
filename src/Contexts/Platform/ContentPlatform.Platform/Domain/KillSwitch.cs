using ContentPlatform.SharedKernel;

namespace ContentPlatform.Platform.Domain;

/// <summary>Kill-switch kapsamı. Key: Channel→platform adı, Category/Account→id; diğerleri null.</summary>
public enum KillScope { Global, Ai, Ingestion, Publishing, Ads, Channel, Category, Account }

/// <summary>Tek bir acil-durdurma anahtarı. (Scope, Key) tekildir; DB'de saklanır (kalıcı + süreçler arası).</summary>
public sealed class KillSwitch : Entity
{
    private KillSwitch() { } // EF

    public KillSwitch(KillScope scope, string? key, bool engaged, string? reason, IClock clock)
    {
        Scope = scope;
        Key = key;
        Engaged = engaged;
        Reason = reason;
        CreatedAt = clock.UtcNow;
    }

    public KillScope Scope { get; private set; }
    public string? Key { get; private set; }
    public bool Engaged { get; private set; }
    public string? Reason { get; private set; }

    public void Set(bool engaged, string? reason, IClock clock)
    {
        Engaged = engaged;
        Reason = reason;
        Touch(clock);
    }
}
