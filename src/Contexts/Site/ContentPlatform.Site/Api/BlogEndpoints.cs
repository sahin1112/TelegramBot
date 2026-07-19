using System.Text;
using ContentPlatform.Abstractions;
using ContentPlatform.Site.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Site.Api;

/// <summary>
/// Public blog (SSR) + SEO uçları. AuthMiddleware yalnız /api/v1/* korur; bu yollar serbesttir.
/// Üst menü ve /kategori/{slug} sayfaları DB kategorilerinden DİNAMİK gelir (IPublicCategoryProvider).
/// </summary>
internal static class BlogEndpoints
{
    private const int PageSize = 10;
    private const string Html = "text/html; charset=utf-8";

    public static void Map(IEndpointRouteBuilder app)
    {
        // ---------------- Ana sayfa ----------------
        // Ana sayfa HEM kökte (/) HEM /blog'da servis edilir. Kök artık REDIRECT DEĞİL: çıplak alan adı
        // (www.hermasadabiz.com) tam HTML döndürür — böylece GTM/GA etiketi kök URL'de de bulunur ve
        // Google etiket doğrulaması (kökü test eder) çalışır. Kanonik URL /blog kalır (tek kaynak).
        app.MapGet("/", HomeHandler).ExcludeFromDescription();
        app.MapGet("/blog", HomeHandler).ExcludeFromDescription();

        // ---------------- Makale ----------------
        app.MapGet("/blog/{slug}", async (string slug, string? ok, HttpRequest req, BlogQueryService q, CommentService comments,
            IPublicCategoryProvider catProvider, IPublicSocialProvider socialProvider, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            var post = await q.BySlugAsync(slug, ct);
            if (post is null) return Results.Content(BlogPages.NotFound(o), Html, Encoding.UTF8, 404);
            // Görüntülenme: botlar/tarayıcı-olmayanlar SAYILMAZ (istatistik şişmesin)
            if (!IsBot(req.Headers.UserAgent.ToString())) await q.IncrementViewAsync(post.Id, ct);
            var cats = await catProvider.GetActiveAsync(ct);
            var cat = post.CategoryId is { } cid ? cats.FirstOrDefault(c => c.Id == cid) : null;
            var related = await q.RelatedAsync(post.Id, post.Tags, 4, ct);
            var appr = await comments.ApprovedForPostAsync(post.Id, ct);
            var popular = await q.TopViewedAsync(4, ct);
            var (prevPost, nextPost) = await q.PrevNextAsync(post.Id, post.PublishedAt, ct);
            return Results.Content(BlogPages.Post(o, post, cat, related, appr, ok == "1", popular, prevPost, nextPost), Html);
        }).ExcludeFromDescription();

        // ---------------- Kategori sayfası (DİNAMİK) ----------------
        app.MapGet("/kategori/{slug}", async (string slug, int? page, BlogQueryService q,
            IPublicCategoryProvider catProvider, IPublicSocialProvider socialProvider, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            var cats = await catProvider.GetActiveAsync(ct);
            var cat = cats.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (cat is null) return Results.Content(BlogPages.NotFound(o), Html, Encoding.UTF8, 404);
            var p = page is > 1 ? page.Value : 1;
            var total = await q.CountByCategoryAsync(cat.Id, ct);
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            var posts = await q.ByCategoryAsync(cat.Id, PageSize, (p - 1) * PageSize, ct);
            return Results.Content(BlogPages.Category(o, cat, posts, p, totalPages), Html);
        }).ExcludeFromDescription();

        // ---------------- Etiket sayfası ----------------
        app.MapGet("/etiket/{tag}", async (string tag, int? page, BlogQueryService q, IPublicCategoryProvider catProvider,
            IPublicSocialProvider socialProvider, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            const int size = 24;
            var p = page is > 1 ? page.Value : 1;
            var (posts, total) = await q.ByTagPagedAsync(tag, size, (p - 1) * size, ct);
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
            return Results.Content(BlogPages.Tag(o, tag, posts, p, totalPages), Html);
        }).ExcludeFromDescription();

        // ---------------- Kurumsal sayfalar (Hakkımızda + İletişim — E-E-A-T/AdSense sinyali) ----------------
        app.MapGet("/hakkimizda", async (IPublicCategoryProvider catProvider, IPublicSocialProvider socialProvider,
            ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            return Results.Content(BlogPages.About(o), Html);
        }).ExcludeFromDescription();

        app.MapGet("/iletisim", async (IPublicCategoryProvider catProvider, IPublicSocialProvider socialProvider,
            ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            return Results.Content(BlogPages.Contact(o), Html);
        }).ExcludeFromDescription();

        // ---------------- Site içi arama ----------------
        app.MapGet("/ara", async (string? q, int? page, BlogQueryService qs, IPublicCategoryProvider catProvider,
            IPublicSocialProvider socialProvider, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            const int size = 24;
            var p = page is > 1 ? page.Value : 1;
            var term = (q ?? "").Trim();
            var (items, total) = term.Length >= 2
                ? await qs.SearchAsync(term, size, (p - 1) * size, ct)
                : (Array.Empty<BlogListItem>(), 0);
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
            return Results.Content(BlogPages.Search(o, term, items, total, p, totalPages), Html);
        }).ExcludeFromDescription();

        // ---------------- Tüm sosyal kanallar ----------------
        app.MapGet("/sosyal", async (IPublicCategoryProvider catProvider, IPublicSocialProvider socialProvider,
            ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            return Results.Content(BlogPages.AllSocials(o), Html);
        }).ExcludeFromDescription();

        // ---------------- Tüm yazılar arşivi (sunucu tarafı sayfalı) ----------------
        app.MapGet("/yazilar", async (int? page, BlogQueryService q, IPublicCategoryProvider catProvider,
            IPublicSocialProvider socialProvider, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            const int size = 24;
            var p = page is > 1 ? page.Value : 1;
            var total = await q.CountAsync(ct);
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
            var posts = await q.RecentAsync(size, (p - 1) * size, ct);
            return Results.Content(BlogPages.AllPosts(o, posts, p, totalPages), Html);
        }).ExcludeFromDescription();

        // ---------------- Mini App kısa kod çözücü ----------------
        // Telegram startapp 64 karakterle sınırlı → gönderi slug yerine KISA kod (ContentItemId) taşır.
        // /r/{kod} bunu gerçek slug'a çevirip makaleye yönlendirir (mini app modunda ?ma=1). GUID değilse
        // kodu doğrudan slug sayar (geriye dönük). Çözülemezse ana sayfaya düşer.
        app.MapGet("/r/{code}", async (string code, BlogQueryService q, CancellationToken ct) =>
        {
            string? slug = Guid.TryParse(code, out var cid)
                ? await q.SlugByContentIdAsync(cid, ct)
                : code;
            return string.IsNullOrWhiteSpace(slug)
                ? Results.Redirect("/blog?ma=1")
                : Results.Redirect($"/blog/{Uri.EscapeDataString(slug)}?ma=1");
        }).ExcludeFromDescription();

        // ---------------- Yasal sayfalar (DÂHİLİ, SSR) ----------------
        app.MapGet("/gizlilik-politikasi", async (IPublicCategoryProvider catProvider, IPublicSocialProvider socialProvider,
            ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            return Results.Content(BlogPages.PrivacyPolicy(o), Html);
        }).ExcludeFromDescription();

        app.MapGet("/cerez-politikasi", async (IPublicCategoryProvider catProvider, IPublicSocialProvider socialProvider,
            ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            return Results.Content(BlogPages.CookiePolicy(o), Html);
        }).ExcludeFromDescription();

        // Kullanım Şartları — TikTok/Meta gibi platform başvuruları "Terms of Service URL" istiyor.
        app.MapGet("/kullanim-sartlari", async (IPublicCategoryProvider catProvider, IPublicSocialProvider socialProvider,
            ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
            return Results.Content(BlogPages.TermsOfService(o), Html);
        }).ExcludeFromDescription();

        // ---------------- SEO çıktıları ----------------
        app.MapGet("/sitemap.xml", async (BlogQueryService q, IPublicCategoryProvider catProvider, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var posts = await q.AllForSitemapAsync(ct);
            var cats = await catProvider.GetActiveAsync(ct);
            var catMod = await q.CategoryLastModifiedAsync(ct);
            var tags = await q.AllTagsAsync(500, ct);
            return Results.Content(BlogPages.Sitemap(opt.Value, posts, cats, catMod, tags), "application/xml; charset=utf-8");
        }).ExcludeFromDescription();

        app.MapGet("/robots.txt", (IOptions<SiteOptions> opt) =>
            Results.Content(BlogPages.Robots(opt.Value), "text/plain; charset=utf-8")).ExcludeFromDescription();

        // Marka logosu (şema publisher.logo + AI/paylaşım önizlemeleri)
        app.MapGet("/logo.svg", (IOptions<SiteOptions> opt) =>
            Results.Content(BlogPages.LogoSvg(opt.Value), "image/svg+xml")).ExcludeFromDescription();

        // PWA manifest (mobil "ana ekrana ekle")
        app.MapGet("/site.webmanifest", (IOptions<SiteOptions> opt) =>
            Results.Content(BlogPages.Manifest(opt.Value), "application/manifest+json")).ExcludeFromDescription();

        // Google News sitemap — son 48 saatin yazıları (Google News / Keşfet görünürlüğü)
        app.MapGet("/news-sitemap.xml", async (BlogQueryService q, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var recent = await q.RecentAsync(100, 0, ct);
            return Results.Content(BlogPages.NewsSitemap(opt.Value, recent, DateTimeOffset.UtcNow), "application/xml; charset=utf-8");
        }).ExcludeFromDescription();

        // llms.txt — AI asistanları için site özeti (llmstxt.org standardı)
        app.MapGet("/llms.txt", async (BlogQueryService q, IPublicCategoryProvider catProvider, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var cats = await catProvider.GetActiveAsync(ct);
            var recent = await q.RecentAsync(20, 0, ct);
            return Results.Content(BlogPages.LlmsTxt(opt.Value, cats, recent), "text/plain; charset=utf-8");
        }).ExcludeFromDescription();

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
            // 1) IndexNow anahtarı (mevcut davranış)
            var configured = await settings.GetAsync("seo.indexnow_key", ct);
            if (string.IsNullOrWhiteSpace(configured)) configured = opt.Value.IndexNowKey;
            if (!string.IsNullOrWhiteSpace(configured) && string.Equals(key, configured, StringComparison.Ordinal))
                return Results.Content(configured, "text/plain; charset=utf-8");

            // 2) GENEL site doğrulama dosyaları (TikTok, Pinterest vb. "dosya yükle" yöntemleri):
            //    Panel → Ayarlar → anahtar: verify.txt.{dosyaadi} , değer: dosyanın İÇERİĞİ.
            //    Örn. TikTok "tiktokAbC123.txt" isterse → anahtar "verify.txt.tiktokAbC123".
            var custom = await settings.GetAsync($"verify.txt.{key}", ct);
            return !string.IsNullOrWhiteSpace(custom)
                ? Results.Content(custom!, "text/plain; charset=utf-8")
                : Results.NotFound();
        }).ExcludeFromDescription();

        // Bülten kaydı (ana sayfa/makale formu). E-postayı Site şemasında saklar (idempotent, migration'sız).
        app.MapPost("/blog/subscribe", async (HttpContext ctx, NewsletterService news, CancellationToken ct) =>
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var email = form["email"].ToString().Trim();
            var ok = await news.SubscribeAsync(email, ct);
            if (ctx.Request.Headers.Accept.ToString().Contains("application/json") ||
                ctx.Request.Headers["X-Requested-With"] == "fetch")
                return Results.Json(new { ok });
            var back = ctx.Request.Headers.Referer.ToString();
            return Results.Redirect(string.IsNullOrWhiteSpace(back) ? "/blog?sub=1" : back + (back.Contains('?') ? "&" : "?") + "sub=1");
        }).ExcludeFromDescription();

        // Telegram Mini App reklam kapisi: startapp=slug -> Adsgram reklami -> /blog/{slug}
        app.MapGet("/ad-gate", async (ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct) =>
        {
            var block = await settings.GetAsync("ads.adsgram_block", ct);
            return Results.Content(BlogPages.AdGatePage(opt.Value, block), "text/html; charset=utf-8");
        }).ExcludeFromDescription();
    }

    /// <summary>
    /// Config'ten bağlanan SiteOptions'ın üzerine DB ayarlarını (home.*/site.*/seo.*) bindirir; böylece ana sayfa/
    /// reklam/sosyal/menü değerleri panelden düzenlenip yeniden deploy'suz güncellenir. Üst menü DB kategorilerinden
    /// DİNAMİK doldurulur (kategori yoksa yedek NavCategories → etiket sayfaları).
    /// </summary>
    /// <summary>Ana sayfa render'ı — hem "/" hem "/blog" için ortak. Kök URL'de de tam sayfa (etiketlerle) döner.</summary>
    private static async Task<IResult> HomeHandler(int? page, BlogQueryService q, IPublicCategoryProvider catProvider,
        IPublicSocialProvider socialProvider, ISettingsProvider settings, IOptions<SiteOptions> opt, CancellationToken ct)
    {
        var o = await EffectiveOptionsAsync(opt.Value, settings, catProvider, socialProvider, ct);
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
        // Yalnız 1. sayfa: en çok okunanlar + kategori vitrinleri (her aktif kategoriden son 3 yazı, ilk 3 kategori)
        IReadOnlyList<BlogListItem> topViewed = Array.Empty<BlogListItem>();
        var catBlocks = new List<(PublicCategory Cat, IReadOnlyList<BlogListItem> Posts)>();
        if (p == 1)
        {
            topViewed = await q.TopViewedAsync(6, ct);
            var cats = await catProvider.GetActiveAsync(ct);
            foreach (var cat in cats.Take(3))
            {
                var cp = await q.ByCategoryAsync(cat.Id, 3, 0, ct);
                if (cp.Count > 0) catBlocks.Add((cat, cp));
            }
        }
        return Results.Content(BlogPages.Home(o, posts, p, totalPages, topViewed, catBlocks), Html);
    }

    private static async Task<SiteOptions> EffectiveOptionsAsync(SiteOptions b, ISettingsProvider s, IPublicCategoryProvider catProvider, IPublicSocialProvider socialProvider, CancellationToken ct)
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

        var navCats = ParseCats(await G("home.categories")) ?? b.NavCategories;

        var o = new SiteOptions
        {
            PublicBaseUrl = b.PublicBaseUrl,
            SiteName = await G("site.name") ?? b.SiteName,
            Description = await G("site.description") ?? b.Description,
            AuthorName = await G("home.author") ?? b.AuthorName,
            AdsEnabled = adsEnabled,
            AdSenseClient = await G("home.adsense_client") ?? b.AdSenseClient,
            AdSenseSlot = await G("home.adsense_slot") ?? b.AdSenseSlot,
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
            ContactEmail = await G("site.contact_email") ?? b.ContactEmail,
            AboutText = await G("site.about_text") ?? b.AboutText,
            // Politika sayfaları artık DÂHİLİ (SSR): panelde özel URL verilmezse yerleşik sayfalara bağlanır.
            PrivacyUrl = await G("seo.privacy_url") ?? (string.IsNullOrWhiteSpace(b.PrivacyUrl) ? "/gizlilik-politikasi" : b.PrivacyUrl),
            CookieUrl = await G("seo.cookie_url") ?? (string.IsNullOrWhiteSpace(b.CookieUrl) ? "/cerez-politikasi" : b.CookieUrl),
            IndexNowKey = await G("seo.indexnow_key") ?? b.IndexNowKey,
            AdsMinWords = minWords,
            HeroSlogan = await G("home.hero_slogan") ?? b.HeroSlogan,
            NavCategories = navCats
        };

        // Üst menü DİNAMİK: aktif DB kategorileri → /kategori/{slug}. Kategori yoksa yedek etiket menüsü.
        List<SiteNavItem> nav;
        try
        {
            var cats = await catProvider.GetActiveAsync(ct);
            nav = cats.Count > 0
                ? cats.Select(c => new SiteNavItem(c.Name, $"/kategori/{Uri.EscapeDataString(c.Slug)}")).ToList()
                : navCats.Select(n => new SiteNavItem(n, $"/etiket/{Uri.EscapeDataString(n)}")).ToList();
        }
        catch { nav = navCats.Select(n => new SiteNavItem(n, $"/etiket/{Uri.EscapeDataString(n)}")).ToList(); }
        o.Nav = nav;

        // Ana sayfa sosyal şeridi: "Sosyal Hesaplar"da 'ana sayfada yayınla' seçili kanallar.
        // Bulunmazsa (hiç seçim yoksa) yedek Site/SEO alanları (TelegramUrl vb.) devreye girer.
        try { o.HomeSocials = await socialProvider.GetHomeLinksAsync(ct); }
        catch { o.HomeSocials = System.Array.Empty<PublicSocialLink>(); }
        return o;
    }

    /// <summary>Bilinen bot/örümcek/önizleme UA'ları — görüntülenme sayacına dahil edilmez.</summary>
    private static bool IsBot(string ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return true;
        var u = ua.ToLowerInvariant();
        return u.Contains("bot") || u.Contains("crawl") || u.Contains("spider") || u.Contains("slurp")
            || u.Contains("preview") || u.Contains("facebookexternalhit") || u.Contains("whatsapp")
            || u.Contains("telegram") || u.Contains("curl") || u.Contains("wget") || u.Contains("python")
            || u.Contains("headless") || u.Contains("lighthouse") || u.Contains("pingdom") || u.Contains("uptime");
    }

    private static IReadOnlyList<string>? ParseCats(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var list = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return list.Length == 0 ? null : list;
    }
}
