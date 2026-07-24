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
        await db.ContentItems.Include(x => x.Revisions).Include(x => x.Media)
            .Where(x => x.EditorialStatus == status)
            .OrderBy(x => x.CreatedAt).Take(take).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<ContentItem>> GetForGenerationAsync(int take, CancellationToken ct) =>
        await db.ContentItems.Include(x => x.Revisions).Include(x => x.Media)
            .Where(x => x.EditorialStatus == Domain.EditorialStatus.Approved &&
                        // 1) Normal akış: görseli henüz üretilmemiş onaylı içerik
                        (x.MediaStatus == Domain.MediaStatus.Pending ||
                         // 2) Takılma düzeltmesi: görseli önizlemeden ÜRETİLMİŞ ama metni (revizyonu) henüz
                         //    olmayan onaylı içerik — metin üretilip MEVCUT görselle yayına gönderilir.
                         //    (Revizyonu VE görseli hazır olanlar onay anında yayınlanır; buraya düşmez.)
                         (x.MediaStatus == Domain.MediaStatus.Ready && !x.Revisions.Any(r => r.IsCurrent))))
            .OrderBy(x => x.CreatedAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetAutoDraftCandidateIdsAsync(int take, CancellationToken ct) =>
        await db.ContentItems
            .Where(x => x.EditorialStatus == Domain.EditorialStatus.Draft &&
                        ((x.AutoContent && x.ContentGen == Domain.GenStepStatus.None) ||
                         (x.AutoImage && x.ImageGen == Domain.GenStepStatus.None) ||
                         (x.AutoVideo && x.VideoGen == Domain.GenStepStatus.None)))
            .OrderBy(x => x.CreatedAt).Take(take).Select(x => x.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<ContentItem>> GetAwaitingManualImageAsync(int take, CancellationToken ct) =>
        await db.ContentItems.Include(x => x.Revisions)
            .Where(x => x.MediaStatus == Domain.MediaStatus.AwaitingManualUpload)
            .OrderBy(x => x.CreatedAt).Take(take).ToListAsync(ct);

    public async Task<(IReadOnlyList<ContentItem> Items, int Total)> GetPagedAsync(Domain.EditorialStatus? status, string? search, Guid? categoryId, bool uncategorized, int page, int size, bool ascending, CancellationToken ct)
    {
        var q = db.ContentItems.Include(x => x.Revisions).Include(x => x.Media).AsQueryable();
        if (status is { } s) q = q.Where(x => x.EditorialStatus == s);
        // Kategori izolasyonu: panelde her kategori yalnız kendi içeriklerini görür.
        if (categoryId is { } cid) q = q.Where(x => x.CategoryId == cid);
        else if (uncategorized) q = q.Where(x => x.CategoryId == null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x =>
                (x.RawTitle != null && x.RawTitle.Contains(term)) ||
                x.Revisions.Any(r => r.IsCurrent && r.Title.Contains(term)));
        }
        var total = await q.CountAsync(ct);
        // Taslak sekmesi eskiden→yeniye (ascending); diğerleri yeniden→eskiye.
        q = ascending ? q.OrderBy(x => x.CreatedAt) : q.OrderByDescending(x => x.CreatedAt);
        var items = await q.Skip(Math.Max(0, page - 1) * size).Take(size).ToListAsync(ct);
        return (items, total);
    }
}
