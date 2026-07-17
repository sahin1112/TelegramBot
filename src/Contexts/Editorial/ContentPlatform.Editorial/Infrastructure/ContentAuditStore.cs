using ContentPlatform.Editorial.Application;
using ContentPlatform.Editorial.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Editorial.Infrastructure;

internal sealed class ContentAuditStore(EditorialDbContext db, IClock clock) : IContentAudit
{
    public void Log(Guid contentItemId, AuditEvent @event, ActorType actorType, string actorRef, string? detail = null) =>
        db.ContentAudit.Add(new ContentAuditEntry(contentItemId, @event, actorType, actorRef, detail, clock));

    public async Task<IReadOnlyList<ContentAuditEntry>> GetTimelineAsync(Guid contentItemId, CancellationToken ct) =>
        await db.ContentAudit.AsNoTracking()
            .Where(a => a.ContentItemId == contentItemId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
}
