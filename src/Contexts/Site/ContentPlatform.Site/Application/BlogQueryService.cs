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
                p.CoverImageAlt, p.Tags, p.PublishedAt, p.UpdatedAt, p.Views, p.CategoryId);
    }

    /// <summary>Site içi arama: başlık + özet içinde geçen ifade (SQL LIKE), yeniden eskiye, sayfalı.</summary>
    public async Task<(IReadOnlyList<BlogListItem> Items, int Total)> SearchAsync(string q, int take, int skip, CancellationToken ct)
    {
        var term = (q ?? "").Trim();
        if (term.Length < 2) return (Array.Empty<BlogListItem>(), 0);
        var query = db.BlogPosts.AsNoTracking()
            .Where(p => p.Title.Contains(term) || p.MetaDescription.Contains(term));
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(p => p.PublishedAt)
            .Skip(skip).Take(take).Select(p => new BlogListItem(p.Slug, p.Title, p.MetaDescription, p.CoverImageUrl, p.PublishedAt, p.Tags))
            .ToListAsync(ct);
        return (items, total);
    }

    /// <summary>Mini App kısa kodu (ContentItemId) → yayınlanmış makalenin slug'ı. Yoksa null.</summary>
    public Task<string?> SlugByContentIdAsync(Guid contentItemId, CancellationToken ct) =>
        db.BlogPosts.AsNoTracking()
            .Where(x => x.ContentItemId == contentItemId)
            .Select(x => (string?)x.Slug)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<BlogListItem>> ByTagAsync(string tag, int take, CancellationToken ct)
    {
        var window = await db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.PublishedAt).Take(TagScanWindow).ToListAsync(ct);
        return window
            .Where(p => p.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            .Take(take).Select(ToListItem).ToList();
    }

    /// <summary>Etiket sayfası için SAYFALI liste (+ toplam). Tarama penceresi içinde eşleşenler.</summary>
    public async Task<(IReadOnlyList<BlogListItem> Items, int Total)> ByTagPagedAsync(string tag, int take, int skip, CancellationToken ct)
    {
        var window = await db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.PublishedAt).Take(TagScanWindow).ToListAsync(ct);
        var all = window
            .Where(p => p.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return (all.Skip(skip).Take(take).Select(ToListItem).ToList(), all.Count);
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

    /// <summary>Bir kategorinin yayınları (yeni→eski, sayfalı).</summary>
    public async Task<IReadOnlyList<BlogListItem>> ByCategoryAsync(Guid categoryId, int take, int skip, CancellationToken ct)
    {
        var rows = await db.BlogPosts.AsNoTracking()
            .Where(p => p.CategoryId == categoryId)
            .OrderByDescending(p => p.PublishedAt).Skip(skip).Take(take).ToListAsync(ct);
        return rows.Select(ToListItem).ToList();
    }

    public Task<int> CountByCategoryAsync(Guid categoryId, CancellationToken ct) =>
        db.BlogPosts.Where(p => p.CategoryId == categoryId).CountAsync(ct);

    /// <summary>Sitemap için: kategori başına en yeni yazı tarihi (yalnız yazısı olan kategoriler).</summary>
    public async Task<IReadOnlyList<(Guid CategoryId, DateTimeOffset LastModified)>> CategoryLastModifiedAsync(CancellationToken ct)
    {
        var rows = await db.BlogPosts.AsNoTracking()
            .Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Last = g.Max(x => x.UpdatedAt ?? x.PublishedAt) })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Last)).ToList();
    }

    /// <summary>Sitemap/iç linkleme için: kullanılan tüm etiketler (iç işaret '_' hariç).</summary>
    public async Task<IReadOnlyList<string>> AllTagsAsync(int scan, CancellationToken ct)
    {
        var rows = await db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.PublishedAt).Take(scan)
            .Select(p => p.Tags).ToListAsync(ct);
        return rows.SelectMany(t => t)
            .Where(t => !string.IsNullOrWhiteSpace(t) && !t.StartsWith('_'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task IncrementViewAsync(Guid id, CancellationToken ct) =>
        await db.BlogPosts.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Views, p => p.Views + 1), ct);

    /// <summary>En çok okunanlar (görüntülenmeye göre; eşitlikte yeni olan önde).</summary>
    public async Task<IReadOnlyList<BlogListItem>> TopViewedAsync(int take, CancellationToken ct)
    {
        var rows = await db.BlogPosts.AsNoTracking()
            .OrderByDescending(p => p.Views).ThenByDescending(p => p.PublishedAt)
            .Take(take).ToListAsync(ct);
        return rows.Select(ToListItem).ToList();
    }

    /// <summary>Makale altı gezinme: bir önceki (daha eski) ve bir sonraki (daha yeni) yazı.</summary>
    public async Task<(BlogListItem? Prev, BlogListItem? Next)> PrevNextAsync(Guid id, DateTimeOffset publishedAt, CancellationToken ct)
    {
        var prev = await db.BlogPosts.AsNoTracking()
            .Where(p => p.Id != id && p.PublishedAt < publishedAt)
            .OrderByDescending(p => p.PublishedAt).FirstOrDefaultAsync(ct);
        var next = await db.BlogPosts.AsNoTracking()
            .Where(p => p.Id != id && p.PublishedAt > publishedAt)
            .OrderBy(p => p.PublishedAt).FirstOrDefaultAsync(ct);
        return (prev is null ? null : ToListItem(prev), next is null ? null : ToListItem(next));
    }

    private static BlogListItem ToListItem(BlogPost p) =>
        new(p.Slug, p.Title, p.MetaDescription, p.CoverImageUrl, p.PublishedAt, p.Tags);
}

public sealed record BlogListItem(string Slug, string Title, string MetaDescription, string? CoverImageUrl, DateTimeOffset PublishedAt, IReadOnlyList<string> Tags);
public sealed record BlogPostView(Guid Id, string Slug, string Title, string MetaDescription, string BodyHtml, string? CoverImageUrl, string? CoverImageAlt, IReadOnlyList<string> Tags, DateTimeOffset PublishedAt, DateTimeOffset? UpdatedAt, long Views, Guid? CategoryId);
public sealed record SitemapEntry(string Slug, DateTimeOffset LastModified);
public sealed record FeedEntry(string Slug, string Title, string Summary, DateTimeOffset PublishedAt);
