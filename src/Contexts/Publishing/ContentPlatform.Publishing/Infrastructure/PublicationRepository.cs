using ContentPlatform.Abstractions;
using ContentPlatform.Publishing.Application;
using ContentPlatform.Publishing.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Publishing.Infrastructure;

internal sealed class PublicationRepository(PublishingDbContext db) : IPublicationRepository
{
    public Task<Publication?> FindAsync(Guid contentItemId, Channel channel, string targetRef, CancellationToken ct) =>
        db.Publications.FirstOrDefaultAsync(x =>
            x.ContentItemId == contentItemId && x.Channel == channel && x.TargetRef == targetRef, ct);

    public Task<Publication?> GetAsync(Guid id, CancellationToken ct) =>
        db.Publications.Include(x => x.DeliveryAttempts).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Publication>> ListAsync(Guid? contentItemId, PublicationStatus? status, int take, CancellationToken ct)
    {
        var q = db.Publications.AsQueryable();
        if (contentItemId is { } cid) q = q.Where(x => x.ContentItemId == cid);
        if (status is { } st) q = q.Where(x => x.Status == st);
        return await q.OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Publication> Items, int Total)> ListPagedAsync(Guid? contentItemId, PublicationStatus? status, string? search, int page, int size, CancellationToken ct)
    {
        var q = db.Publications.AsQueryable();
        if (contentItemId is { } cid) q = q.Where(x => x.ContentItemId == cid);
        if (status is { } st) q = q.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => x.TargetRef.Contains(term) || (x.Error != null && x.Error.Contains(term)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.CreatedAt)
            .Skip(Math.Max(0, page - 1) * size).Take(size).ToListAsync(ct);
        return (items, total);
    }

    public async Task AddAsync(Publication publication, CancellationToken ct) =>
        await db.Publications.AddAsync(publication, ct);

    public async Task<IReadOnlyList<Publication>> GetStuckPendingAsync(DateTimeOffset olderThan, int take, CancellationToken ct) =>
        await db.Publications
            .Where(x => x.Status == PublicationStatus.Pending && (x.UpdatedAt ?? x.CreatedAt) <= olderThan)
            .OrderBy(x => x.CreatedAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<Publication>> GetDueScheduledAsync(DateTimeOffset now, int take, CancellationToken ct) =>
        await db.Publications
            .Where(x => x.Status == PublicationStatus.Scheduled && x.ScheduledAt != null && x.ScheduledAt <= now)
            .OrderBy(x => x.ScheduledAt).Take(take).ToListAsync(ct);

    public async Task<bool> TryClaimScheduledAsync(Guid id, CancellationToken ct) =>
        await db.Publications
            .Where(x => x.Id == id && x.Status == PublicationStatus.Scheduled)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PublicationStatus.Pending), ct) == 1;

    public async Task<IReadOnlyList<Publication>> GetScheduledAsync(int take, CancellationToken ct) =>
        await db.Publications
            .Where(x => x.Status == PublicationStatus.Scheduled)
            .OrderBy(x => x.ScheduledAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<DateTimeOffset>> GetScheduledTimesForCategoryAsync(Guid? categoryId, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-2); // kadans için yakın geçmiş yeterli (günlük tavan + aralık)
        return await db.Publications
            .Where(x => x.CategoryId == categoryId && (
                (x.Status == PublicationStatus.Scheduled && x.ScheduledAt != null) ||
                (x.Status == PublicationStatus.Pending && x.CreatedAt >= since) ||
                (x.Status == PublicationStatus.Published && x.PublishedAt != null && x.PublishedAt >= since)))
            .Select(x => x.Status == PublicationStatus.Published
                ? (x.ScheduledAt ?? x.PublishedAt!.Value)   // planlı gönderildiyse slot zamanı esas
                : (x.ScheduledAt ?? x.CreatedAt))           // Pending = şimdi gönderiliyor sayılır
            .Distinct()
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
