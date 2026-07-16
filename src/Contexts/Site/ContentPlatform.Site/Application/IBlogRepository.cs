using ContentPlatform.Site.Domain;

namespace ContentPlatform.Site.Application;

/// <summary>Blog yazısı yazma tarafı (idempotent upsert için).</summary>
public interface IBlogRepository
{
    Task<BlogPost?> GetByContentItemAsync(Guid contentItemId, CancellationToken ct);
    Task AddAsync(BlogPost post, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
