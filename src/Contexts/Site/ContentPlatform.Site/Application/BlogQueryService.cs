using ContentPlatform.Site.Domain;
using ContentPlatform.Site.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Site.Application;

/// <summary>
/// Blog okuma tarafı — SSR sayfaları ve SEO çıktıları için sorgular.
/// Not: Tags tek kolonda (value converter) saklandığından, etiket filtreleri SQL'de değil
/// sınırlı bir aday kümesi çekilip BELLEKTE uygulanır (MVP ölçeğinde yeterli).
/// </summary>
public sealed class BlogQueryService(SiteDbContext db)
{
    private const int TagScanWindow = 500;

    public async Task<IReadOnlyList<BlogListItem>> RecentAsync(int take, int skip, CancellationToken ct)
    {
        var rows = await db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.PublishedAt).Skip(skip).Take(take).ToListAsync(ct);
        return rows.Select(ToListItem).ToList();
    }

    public Task<int> CountAsync(CancellationToken ct) => db.BlogPosts.CountAsync(ct);

    public async Task<BlogPostView?> BySlugAsync(string slug, CancellationToken ct)
    {
        var p = await db.BlogPosts.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
        return p is null
            ? null
            : new BlogPostView(p.Id, p.Slug, p.Title, p.MetaDescription, p.BodyHtml, p.CoverImageUrl,
                p.CoverImageAlt, p.Tags, p.PublishedAt, p.UpdatedAt, p.Views);
    }

    public async Task<IReadOnlyList<BlogListItem>> ByTagAsync(string tag, int take, CancellationToken ct)
    {
        var window = await db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.PublishedAt).Take(TagScanWindow).ToListAsync(ct);
        return window
            .Where(p => p.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            .Take(take).Select(ToListItem).ToList();
    }

    /// <summary>Ortak etiketi olan diğer yazılar (iç linkleme).</summary>
    public async Task<IReadOnlyList<BlogListItem>> RelatedAsync(Guid excludeId, IReadOnlyList<string> tags, int take, CancellationToken ct)
    {
        if (tags.Count == 0) return Array.Empty<BlogListItem>();
        var window = await db.BlogPosts.AsNoTracking()
            .Where(p => p.Id != excludeId)
            .OrderByDescending(p => p.PublishedAt).Take(50).ToListAsync(ct);
        return window
            .Where(p => p.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            .Take(take).Select(ToListItem).ToList();
    }

    public async Task<IReadOnlyList<SitemapEntry>> AllForSitemapAsync(CancellationToken ct) =>
        await db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.PublishedAt)
            .Select(p => new SitemapEntry(p.Slug, p.UpdatedAt ?? p.PublishedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FeedEntry>> RecentForFeedAsync(int take, CancellationToken ct) =>
        await db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.PublishedAt).Take(take)
            .Select(p => new FeedEntry(p.Slug, p.Title, p.MetaDescription, p.PublishedAt))
            .ToListAsync(ct);

    public async Task IncrementViewAsync(Guid id, CancellationToken ct) =>
        await db.BlogPosts.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Views, p => p.Views + 1), ct);

    private static BlogListItem ToListItem(BlogPost p) =>
        new(p.Slug, p.Title, p.MetaDescription, p.CoverImageUrl, p.PublishedAt, p.Tags);
}

public sealed record BlogListItem(string Slug, string Title, string MetaDescription, string? CoverImageUrl, DateTimeOffset PublishedAt, IReadOnlyList<string> Tags);
public sealed record BlogPostView(Guid Id, string Slug, string Title, string MetaDescription, string BodyHtml, string? CoverImageUrl, string? CoverImageAlt, IReadOnlyList<string> Tags, DateTimeOffset PublishedAt, DateTimeOffset? UpdatedAt, long Views);
public sealed record SitemapEntry(string Slug, DateTimeOffset LastModified);
public sealed record FeedEntry(string Slug, string Title, string Summary, DateTimeOffset PublishedAt);
