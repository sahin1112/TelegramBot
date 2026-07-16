using ContentPlatform.Ingestion.Contracts;
using ContentPlatform.Ingestion.Domain;

namespace ContentPlatform.Ingestion.Application;

public interface ISourceRepository
{
    Task<Source?> GetAsync(Guid id, CancellationToken ct);
    Task AddAsync(Source source, CancellationToken ct);
    void Remove(Source source);
    Task<IReadOnlyList<Source>> ListAsync(CancellationToken ct);
    Task<IReadOnlyList<Source>> ListDueAsync(DateTimeOffset now, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Kaynak okuma portu (RSS/sayfa). Adaptör Infrastructure'da.</summary>
public interface IFeedReader
{
    bool CanRead(SourceType type);
    Task<IReadOnlyList<RawFeedItem>> ReadAsync(Source source, CancellationToken ct);
}

/// <summary>Tekilleştirme portu.</summary>
public interface IDedupStore
{
    Task<bool> HasSeenAsync(string sourceHash, CancellationToken ct);
    Task MarkSeenAsync(string sourceHash, Guid sourceId, CancellationToken ct);
}

/// <summary>Ham öğeden FactPack (kaynak & olgu) çıkarır.</summary>
public interface IFactPackExtractor
{
    FactPack Extract(Source source, RawFeedItem item);
}
