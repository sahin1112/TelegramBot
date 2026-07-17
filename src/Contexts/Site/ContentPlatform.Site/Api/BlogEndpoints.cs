using ContentPlatform.Abstractions;
using ContentPlatform.Site.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Site.Api;

/// <summary>Public blog (SSR) + SEO uçları. AuthMiddleware yalnız /api/v1/* korur; bu yollar serbesttir.</summary>
internal static class BlogEndpoints
{
    private const int PageSize = 10;

    public static void Map(IEndpointRouteBuilder app)
    {
        const string Html = "text/html; charset=utf-8";

        app.MapGet("/blog", async (int? page, BlogQueryService q, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = opt.Value;
            var p = page is > 1 ? page.Value : 1;
            var total = await q.CountAsync(ct);
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            var posts = await q.RecentAsync(PageSize, (p - 1) * PageSize, ct);
            return Results.Content(BlogPages.Home(o, posts, p, totalPages), Html);
        }).ExcludeFromDescription();

        app.MapGet("/blog/{slug}", async (string slug, string? ok, BlogQueryService q, CommentService comments, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = opt.Value;
            var post = await q.BySlugAsync(slug, ct);
            if (post is null) return Results.Content(BlogPages.NotFound(o), Html, System.Text.Encoding.UTF8, 404);
            await q.IncrementViewAsync(post.Id, ct);
            var related = await q.RelatedAsync(post.Id, post.Tags, 4, ct);
            var appr = await comments.ApprovedForPostAsync(post.Id, ct);
            return Results.Content(BlogPages.Post(o, post, related, appr, ok == "1"), Html);
        }).ExcludeFromDescription();

        app.MapGet("/etiket/{tag}", async (string tag, BlogQueryService q, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = opt.Value;
            var posts = await q.ByTagAsync(tag, 50, ct);
            return Results.Content(BlogPages.Tag(o, tag, posts), Html);
        }).ExcludeFromDescription();

        app.MapGet("/sitemap.xml", async (BlogQueryService q, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var entries = await q.AllForSitemapAsync(ct);
            return Results.Content(BlogPages.Sitemap(opt.Value, entries), "application/xml; charset=utf-8");
        }).ExcludeFromDescription();

        app.MapGet("/robots.txt", (IOptions<SiteOptions> opt) =>
            Results.Content(BlogPages.Robots(opt.Value), "text/plain; charset=utf-8")).ExcludeFromDescription();

        app.MapGet("/feed.xml", async (BlogQueryService q, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var entries = await q.RecentForFeedAsync(30, ct);
            return Results.Content(BlogPages.Feed(opt.Value, entries), "application/rss+xml; charset=utf-8");
        }).ExcludeFromDescription();
    }
}
