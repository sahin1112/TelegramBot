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

    public async Task AddAsync(Publication publication, CancellationToken ct) =>
        await db.Publications.AddAsync(publication, ct);

    public async Task<IReadOnlyList<Publication>> GetRetriableAsync(int maxAttempts, int take, CancellationToken ct) =>
        await db.Publications
            .Where(x => x.Status == PublicationStatus.Failed && x.Attempts < maxAttempts)
            .OrderBy(x => x.CreatedAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<Publication>> GetDueScheduledAsync(DateTimeOffset now, int take, CancellationToken ct) =>
        await db.Publications
            .Where(x => x.Status == PublicationStatus.Scheduled && x.ScheduledAt != null && x.ScheduledAt <= now)
            .OrderBy(x => x.ScheduledAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<Publication>> GetScheduledAsync(int take, CancellationToken ct) =>
        await db.Publications
            .Where(x => x.Status == PublicationStatus.Scheduled)
            .OrderBy(x => x.ScheduledAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<DateTimeOffset>> GetScheduledTimesForCategoryAsync(Guid? categoryId, CancellationToken ct) =>
        await db.Publications
            .Where(x => x.Status == PublicationStatus.Scheduled && x.ScheduledAt != null && x.CategoryId == categoryId)
            .Select(x => x.ScheduledAt!.Value)
            .Distinct()
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
