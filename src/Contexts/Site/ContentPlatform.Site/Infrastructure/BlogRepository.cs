using ContentPlatform.Site.Application;
using ContentPlatform.Site.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Site.Infrastructure;

internal sealed class BlogRepository(SiteDbContext db) : IBlogRepository
{
    public Task<BlogPost?> GetByContentItemAsync(Guid contentItemId, CancellationToken ct) =>
        db.BlogPosts.FirstOrDefaultAsync(p => p.ContentItemId == contentItemId, ct);

    public async Task AddAsync(BlogPost post, CancellationToken ct) =>
        await db.BlogPosts.AddAsync(post, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
