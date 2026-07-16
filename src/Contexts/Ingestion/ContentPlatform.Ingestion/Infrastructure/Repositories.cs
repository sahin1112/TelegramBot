using ContentPlatform.Ingestion.Application;
using ContentPlatform.Ingestion.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Ingestion.Infrastructure;

internal sealed class SourceRepository(IngestionDbContext db) : ISourceRepository
{
    public Task<Source?> GetAsync(Guid id, CancellationToken ct) =>
        db.Sources.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(Source source, CancellationToken ct) => await db.Sources.AddAsync(source, ct);

    public void Remove(Source source) => db.Sources.Remove(source);

    public async Task<IReadOnlyList<Source>> ListAsync(CancellationToken ct) =>
        await db.Sources.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Source>> ListDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Kaba filtre DB'de; kesin "due" kararı domain'de (IsDue).
        var candidates = await db.Sources.Where(x => x.IsActive).ToListAsync(ct);
        return candidates.Where(x => x.IsDue(now)).ToList();
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class DedupStore(IngestionDbContext db, IClock clock) : IDedupStore
{
    public Task<bool> HasSeenAsync(string sourceHash, CancellationToken ct) =>
        db.SeenItems.AnyAsync(x => x.SourceHash == sourceHash, ct);

    public async Task MarkSeenAsync(string sourceHash, Guid sourceId, CancellationToken ct) =>
        await db.SeenItems.AddAsync(new SeenItem(sourceHash, sourceId, clock), ct);
}
