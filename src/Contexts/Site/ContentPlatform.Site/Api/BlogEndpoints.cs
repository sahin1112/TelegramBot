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

        app.MapGet("/blog", async (int? page, BlogQueryService q, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, ct);
            var p = page is > 1 ? page.Value : 1;
            var total = await q.CountAsync(ct);
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            var posts = await q.RecentAsync(PageSize, (p - 1) * PageSize, ct);

            // Öne çıkan: admin bir slug girdiyse (home.featured_slug) onu 1. sıraya al; yoksa en yeni kalır.
            if (p == 1)
            {
                var fs = await settings.GetAsync("home.featured_slug", ct);
                if (!string.IsNullOrWhiteSpace(fs))
                {
                    var fp = await q.BySlugAsync(fs, ct);
                    if (fp is not null)
                    {
                        var featured = new BlogListItem(fp.Slug, fp.Title, fp.MetaDescription, fp.CoverImageUrl, fp.PublishedAt, fp.Tags);
                        posts = new[] { featured }
                            .Concat(posts.Where(x => !string.Equals(x.Slug, fp.Slug, StringComparison.OrdinalIgnoreCase)))
                            .Take(PageSize).ToList();
                    }
                }
            }
            return Results.Content(BlogPages.Home(o, posts, p, totalPages), Html);
        }).ExcludeFromDescription();

        app.MapGet("/blog/{slug}", async (string slug, string? ok, BlogQueryService q, CommentService comments, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, ct);
            var post = await q.BySlugAsync(slug, ct);
            if (post is null) return Results.Content(BlogPages.NotFound(o), Html, System.Text.Encoding.UTF8, 404);
            await q.IncrementViewAsync(post.Id, ct);
            var related = await q.RelatedAsync(post.Id, post.Tags, 4, ct);
            var appr = await comments.ApprovedForPostAsync(post.Id, ct);
            return Results.Content(BlogPages.Post(o, post, related, appr, ok == "1"), Html);
        }).ExcludeFromDescription();

        app.MapGet("/etiket/{tag}", async (string tag, BlogQueryService q, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, ct);
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

        // ads.txt (reklam uyumu) — panelden 'seo.ads_txt' ayarına yazılan içerik
        app.MapGet("/ads.txt", async (ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var content = await settings.GetAsync("seo.ads_txt", ct);
            if (string.IsNullOrWhiteSpace(content)) content = opt.Value.AdsTxt;
            return Results.Content(content ?? "", "text/plain; charset=utf-8");
        }).ExcludeFromDescription();

        // IndexNow anahtar dosyasi: /{key}.txt -> icerik = key (yalniz yapilandirilmis anahtarla eslesirse)
        app.MapGet("/{key}.txt", async (string key, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var configured = await settings.GetAsync("seo.indexnow_key", ct);
            if (string.IsNullOrWhiteSpace(configured)) configured = opt.Value.IndexNowKey;
            return !string.IsNullOrWhiteSpace(configured) && string.Equals(key, configured, StringComparison.Ordinal)
                ? Results.Content(configured, "text/plain; charset=utf-8")
                : Results.NotFound();
        }).ExcludeFromDescription();

        // Telegram Mini App reklam kapisi: startapp=slug -> Adsgram reklami -> /blog/{slug}
        app.MapGet("/ad-gate", async (ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var block = await settings.GetAsync("ads.adsgram_block", ct);
            return Results.Content(BlogPages.AdGatePage(opt.Value, block), "text/html; charset=utf-8");
        }).ExcludeFromDescription();
    }

    /// <summary>
    /// Config'ten bağlanan SiteOptions'ın üzerine DB ayarlarını (home.*) bindirir; böylece ana sayfa/reklam/sosyal
    /// değerleri panelden düzenlenip yeniden deploy'suz güncellenir. Ayar yoksa config değeri kalır.
    /// </summary>
    private static async Task<SiteOptions> EffectiveOptionsAsync(SiteOptions b, ISettingsProvider s, CancellationToken ct)
    {
        async Task<string?> G(string k)
        {
            var v = await s.GetAsync(k, ct);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }
        var adsRaw = await G("home.ads_enabled");
        var adsEnabled = adsRaw is null ? b.AdsEnabled : (adsRaw is "1" or "true" or "True" or "on");
        var crRaw = await G("seo.consent_required");
        var consent = crRaw is null ? b.ConsentRequired : (crRaw is "1" or "true" or "on");
        var minWords = int.TryParse(await G("seo.ads_min_words"), out var mw) ? mw : b.AdsMinWords;

        return new SiteOptions
        {
            PublicBaseUrl = b.PublicBaseUrl,
            SiteName = await G("site.name") ?? b.SiteName,
            Description = await G("site.description") ?? b.Description,
            AuthorName = await G("home.author") ?? b.AuthorName,
            AdsEnabled = adsEnabled,
            AdSenseClient = await G("home.adsense_client") ?? b.AdSenseClient,
            TelegramUrl = await G("home.telegram_url") ?? b.TelegramUrl,
            TelegramMembers = await G("home.telegram_members") ?? b.TelegramMembers,
            XUrl = await G("home.x_url") ?? b.XUrl,
            XFollowers = await G("home.x_followers") ?? b.XFollowers,
            InstagramUrl = await G("home.instagram_url") ?? b.InstagramUrl,
            InstagramFollowers = await G("home.instagram_followers") ?? b.InstagramFollowers,
            ThreadsUrl = await G("home.threads_url") ?? b.ThreadsUrl,
            ThreadsFollowers = await G("home.threads_followers") ?? b.ThreadsFollowers,
            YoutubeUrl = await G("home.youtube_url") ?? b.YoutubeUrl,
            YoutubeSubscribers = await G("home.youtube_subscribers") ?? b.YoutubeSubscribers,
            Ga4Id = await G("seo.ga4_id") ?? b.Ga4Id,
            GtmId = await G("seo.gtm_id") ?? b.GtmId,
            GscVerification = await G("seo.gsc_verification") ?? b.GscVerification,
            AdsTxt = await G("seo.ads_txt") ?? b.AdsTxt,
            ConsentRequired = consent,
            PrivacyUrl = await G("seo.privacy_url") ?? b.PrivacyUrl,
            CookieUrl = await G("seo.cookie_url") ?? b.CookieUrl,
            IndexNowKey = await G("seo.indexnow_key") ?? b.IndexNowKey,
            AdsMinWords = minWords
        };
    }
}
