using ContentPlatform.SharedKernel;

namespace ContentPlatform.Ingestion.Domain;

/// <summary>Tekilleştirme kaydı: aynı haber iki kez işlenmez (SourceHash UNIQUE).</summary>
public sealed class SeenItem : Entity
{
    private SeenItem() { }
    public SeenItem(string sourceHash, Guid sourceId, IClock clock)
    {
        SourceHash = sourceHash;
        SourceId = sourceId;
        CreatedAt = clock.UtcNow;
    }
    public string SourceHash { get; private set; } = default!;
    public Guid SourceId { get; private set; }
}
