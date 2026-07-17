using ContentPlatform.Editorial.Application;
using ContentPlatform.Editorial.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Editorial.Infrastructure;

internal sealed class ContentRepository(EditorialDbContext db) : IContentRepository
{
    public Task<ContentItem?> GetAsync(Guid id, CancellationToken ct) =>
        db.ContentItems.Include(x => x.Revisions).Include(x => x.Media).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> ExistsByHashAsync(string sourceHash, CancellationToken ct) =>
        db.ContentItems.AnyAsync(x => x.SourceHash == sourceHash, ct);

    public async Task AddAsync(ContentItem item, CancellationToken ct) =>
        await db.ContentItems.AddAsync(item, ct);

    public async Task<IReadOnlyList<ContentItem>> GetByStatusAsync(EditorialStatus status, int take, CancellationToken ct) =>
        await db.ContentItems.Where(x => x.EditorialStatus == status)
            .OrderBy(x => x.CreatedAt).Take(take).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<ContentItem>> GetForGenerationAsync(int take, CancellationToken ct) =>
        await db.ContentItems.Include(x => x.Revisions)
            .Where(x => x.EditorialStatus == Domain.EditorialStatus.Approved && x.MediaStatus == Domain.MediaStatus.Pending)
            .OrderBy(x => x.CreatedAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<ContentItem>> GetAwaitingManualImageAsync(int take, CancellationToken ct) =>
        await db.ContentItems.Include(x => x.Revisions)
            .Where(x => x.MediaStatus == Domain.MediaStatus.AwaitingManualUpload)
            .OrderBy(x => x.CreatedAt).Take(take).ToListAsync(ct);

    public async Task<(IReadOnlyList<ContentItem> Items, int Total)> GetPagedAsync(Domain.EditorialStatus? status, string? search, int page, int size, CancellationToken ct)
    {
        var q = db.ContentItems.Include(x => x.Revisions).AsQueryable();
        if (status is { } s) q = q.Where(x => x.EditorialStatus == s);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x =>
                (x.RawTitle != null && x.RawTitle.Contains(term)) ||
                x.Revisions.Any(r => r.IsCurrent && r.Title.Contains(term)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.CreatedAt)
            .Skip(Math.Max(0, page - 1) * size).Take(size).ToListAsync(ct);
        return (items, total);
    }
}
