using System.Net;
using System.Text;
using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Site.Application;

namespace ContentPlatform.Site.Api;

/// <summary>Sunucu tarafı (SSR) HTML üretimi + SEO çıktıları. Bağımlılık yok; saf string render.</summary>
internal static class BlogPages
{
    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Abs(SiteOptions o, string path) => $"{o.BaseUrlTrimmed}{path}";

    // ---------- Ortak iskelet ----------
    // CSS interpolasyonsuz ayrı raw sabitte (parantez kaçışı gerekmez; raw interpolated string + '{{' CS9006 verir).
    private const string BaseCss = """
    :root{--bg:#fff;--fg:#1b1f24;--muted:#5b6472;--line:#e6e8ec;--accent:#1a7f6b;--card:#f7f8fa}
    @media(prefers-color-scheme:dark){:root{--bg:#0f1420;--fg:#e6ebf5;--muted:#8a95a8;--line:#273246;--accent:#2ec5b6;--card:#171d2b}}
    *{box-sizing:border-box}
    body{margin:0;font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif;background:var(--bg);color:var(--fg);line-height:1.65}
    a{color:var(--accent);text-decoration:none} a:hover{text-decoration:underline}
    .wrap{max-width:760px;margin:0 auto;padding:0 20px}
    header.site{border-bottom:1px solid var(--line);padding:18px 0;margin-bottom:8px}
    header.site .wrap{display:flex;align-items:center;justify-content:space-between}
    header.site a.logo{font-weight:700;font-size:19px;color:var(--fg)}
    header.site nav a{margin-left:16px;color:var(--muted)}
    h1{font-size:30px;line-height:1.25;margin:18px 0 8px}
    article h2{font-size:22px;margin:28px 0 8px} article h3{font-size:18px;margin:22px 0 6px}
    .meta{color:var(--muted);font-size:14px;margin-bottom:18px}
    .cover{width:100%;height:auto;border-radius:12px;margin:10px 0 6px}
    article img{max-width:100%;height:auto;border-radius:10px}
    article p{margin:14px 0}
    .tags{margin:22px 0;display:flex;flex-wrap:wrap;gap:8px}
    .tags a{background:var(--card);border:1px solid var(--line);border-radius:20px;padding:4px 12px;font-size:13px;color:var(--muted)}
    .cards{list-style:none;padding:0;margin:14px 0;display:grid;gap:14px}
    .cards li{border:1px solid var(--line);border-radius:12px;padding:16px;background:var(--card)}
    .cards h2{font-size:18px;margin:0 0 6px} .cards .meta{margin:0}
    .related{border-top:1px solid var(--line);margin-top:34px;padding-top:18px}
    footer.site{border-top:1px solid var(--line);margin-top:40px;padding:24px 0;color:var(--muted);font-size:14px}
    .pager{display:flex;justify-content:space-between;margin:24px 0}
    """;

    private static string Layout(SiteOptions o, string headExtra, string bodyInner)
    {
        return $"""
        <!DOCTYPE html>
        <html lang="tr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        {headExtra}
        <style>
        {BaseCss}
        </style>
        </head>
        <body>
        <header class="site"><div class="wrap"><a class="logo" href="/blog">{Enc(o.SiteName)}</a>
          <nav><a href="/blog">Blog</a><a href="/feed.xml">RSS</a></nav></div></header>
        <main class="wrap">{bodyInner}</main>
        <footer class="site"><div class="wrap">© {DateTimeOffset.UtcNow.Year} {Enc(o.SiteName)} · <a href="/feed.xml">RSS</a> · <a href="/sitemap.xml">Site haritası</a></div></footer>
        </body></html>
        """;
    }

    private static string SeoHead(SiteOptions o, string title, string description, string canonicalPath, string? imageUrl, string type, string? jsonLd)
    {
        var canonical = Abs(o, canonicalPath);
        var img = imageUrl is null ? "" : (imageUrl.StartsWith("http") ? imageUrl : Abs(o, imageUrl));
        var sb = new StringBuilder();
        sb.Append($"<title>{Enc(title)}</title>\n");
        sb.Append($"<meta name=\"description\" content=\"{Enc(description)}\">\n");
        sb.Append($"<link rel=\"canonical\" href=\"{Enc(canonical)}\">\n");
        sb.Append("<meta name=\"robots\" content=\"index,follow\">\n");
        sb.Append($"<meta property=\"og:type\" content=\"{type}\">\n");
        sb.Append($"<meta property=\"og:title\" content=\"{Enc(title)}\">\n");
        sb.Append($"<meta property=\"og:description\" content=\"{Enc(description)}\">\n");
        sb.Append($"<meta property=\"og:url\" content=\"{Enc(canonical)}\">\n");
        sb.Append($"<meta property=\"og:site_name\" content=\"{Enc(o.SiteName)}\">\n");
        if (img.Length > 0) sb.Append($"<meta property=\"og:image\" content=\"{Enc(img)}\">\n");
        sb.Append($"<meta name=\"twitter:card\" content=\"{(img.Length > 0 ? "summary_large_image" : "summary")}\">\n");
        sb.Append($"<meta name=\"twitter:title\" content=\"{Enc(title)}\">\n");
        sb.Append($"<meta name=\"twitter:description\" content=\"{Enc(description)}\">\n");
        if (img.Length > 0) sb.Append($"<meta name=\"twitter:image\" content=\"{Enc(img)}\">\n");
        sb.Append($"<link rel=\"alternate\" type=\"application/rss+xml\" title=\"{Enc(o.SiteName)}\" href=\"{Enc(Abs(o, "/feed.xml"))}\">\n");
        if (jsonLd is not null) sb.Append($"<script type=\"application/ld+json\">{jsonLd}</script>\n");
        return sb.ToString();
    }

    // ---------- Sayfalar ----------
    public static string Home(SiteOptions o, IReadOnlyList<BlogListItem> posts, int page, int totalPages)
    {
        var head = SeoHead(o, page > 1 ? $"{o.SiteName} — Sayfa {page}" : o.SiteName, o.Description, page > 1 ? $"/blog?page={page}" : "/blog", null, "website", null);
        var body = new StringBuilder();
        body.Append($"<h1>{Enc(o.SiteName)}</h1><p class=\"meta\">{Enc(o.Description)}</p>");
        body.Append(Cards(o, posts));
        body.Append(Pager(page, totalPages, "/blog"));
        return Layout(o, head, body.ToString());
    }

    public static string Post(SiteOptions o, BlogPostView p, IReadOnlyList<BlogListItem> related)
    {
        var path = $"/blog/{p.Slug}";
        var jsonLd = ArticleJsonLd(o, p);
        var head = SeoHead(o, p.Title, p.MetaDescription, path, p.CoverImageUrl, "article", jsonLd);
        var body = new StringBuilder();
        body.Append("<article>");
        body.Append($"<h1>{Enc(p.Title)}</h1>");
        body.Append($"<div class=\"meta\">{p.PublishedAt.ToLocalTime():dd MMMM yyyy}</div>");
        if (!string.IsNullOrEmpty(p.CoverImageUrl))
            body.Append($"<img class=\"cover\" src=\"{Enc(p.CoverImageUrl)}\" alt=\"{Enc(p.CoverImageAlt ?? p.Title)}\">");
        body.Append(p.BodyHtml); // sanitize edilmiş
        if (p.Tags.Count > 0)
        {
            body.Append("<div class=\"tags\">");
            foreach (var t in p.Tags) body.Append($"<a href=\"/etiket/{Enc(Uri.EscapeDataString(t))}\">#{Enc(t)}</a>");
            body.Append("</div>");
        }
        body.Append("</article>");
        if (related.Count > 0)
        {
            body.Append("<section class=\"related\"><h2>İlgili yazılar</h2>");
            body.Append(Cards(o, related));
            body.Append("</section>");
        }
        return Layout(o, head, body.ToString());
    }

    public static string Tag(SiteOptions o, string tag, IReadOnlyList<BlogListItem> posts)
    {
        var head = SeoHead(o, $"#{tag} — {o.SiteName}", $"{tag} etiketli yazılar", $"/etiket/{Uri.EscapeDataString(tag)}", null, "website", null);
        var body = $"<h1>#{Enc(tag)}</h1><p class=\"meta\">{posts.Count} yazı</p>{Cards(o, posts)}";
        return Layout(o, head, body);
    }

    public static string NotFound(SiteOptions o)
    {
        var head = $"<title>Bulunamadı — {Enc(o.SiteName)}</title><meta name=\"robots\" content=\"noindex\">";
        return Layout(o, head, "<h1>Sayfa bulunamadı</h1><p><a href=\"/blog\">Bloga dön</a></p>");
    }

    private static string Cards(SiteOptions o, IReadOnlyList<BlogListItem> posts)
    {
        if (posts.Count == 0) return "<p class=\"meta\">Henüz yazı yok.</p>";
        var sb = new StringBuilder("<ul class=\"cards\">");
        foreach (var p in posts)
            sb.Append($"<li><h2><a href=\"/blog/{Enc(p.Slug)}\">{Enc(p.Title)}</a></h2>" +
                      $"<div class=\"meta\">{p.PublishedAt.ToLocalTime():dd MMMM yyyy}</div>" +
                      $"<p>{Enc(p.MetaDescription)}</p></li>");
        sb.Append("</ul>");
        return sb.ToString();
    }

    private static string Pager(int page, int totalPages, string basePath)
    {
        if (totalPages <= 1) return "";
        var prev = page > 1 ? $"<a href=\"{basePath}?page={page - 1}\">← Önceki</a>" : "<span></span>";
        var next = page < totalPages ? $"<a href=\"{basePath}?page={page + 1}\">Sonraki →</a>" : "<span></span>";
        return $"<div class=\"pager\">{prev}{next}</div>";
    }

    private static string ArticleJsonLd(SiteOptions o, BlogPostView p)
    {
        var url = Abs(o, $"/blog/{p.Slug}");
        var article = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Article",
            ["headline"] = p.Title,
            ["description"] = p.MetaDescription,
            ["datePublished"] = p.PublishedAt.ToString("o"),
            ["dateModified"] = (p.UpdatedAt ?? p.PublishedAt).ToString("o"),
            ["mainEntityOfPage"] = url,
            ["url"] = url,
            ["publisher"] = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = o.SiteName }
        };
        if (!string.IsNullOrEmpty(p.CoverImageUrl))
            article["image"] = p.CoverImageUrl!.StartsWith("http") ? p.CoverImageUrl : Abs(o, p.CoverImageUrl);

        var breadcrumb = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BreadcrumbList",
            ["itemListElement"] = new object[]
            {
                new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 1, ["name"] = "Blog", ["item"] = Abs(o, "/blog") },
                new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 2, ["name"] = p.Title, ["item"] = url }
            }
        };
        var opts = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return JsonSerializer.Serialize(new[] { article, breadcrumb }, opts);
    }

    // ---------- SEO çıktıları ----------
    public static string Sitemap(SiteOptions o, IReadOnlyList<SitemapEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        sb.Append($"<url><loc>{Enc(Abs(o, "/blog"))}</loc></url>\n");
        foreach (var e in entries)
            sb.Append($"<url><loc>{Enc(Abs(o, $"/blog/{e.Slug}"))}</loc><lastmod>{e.LastModified:yyyy-MM-dd}</lastmod></url>\n");
        sb.Append("</urlset>");
        return sb.ToString();
    }

    public static string Robots(SiteOptions o) =>
        $"User-agent: *\nAllow: /\nSitemap: {Abs(o, "/sitemap.xml")}\n";

    public static string Feed(SiteOptions o, IReadOnlyList<FeedEntry> entries)
    {
        string X(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<rss version=\"2.0\"><channel>\n");
        sb.Append($"<title>{X(o.SiteName)}</title>\n<link>{X(Abs(o, "/blog"))}</link>\n<description>{X(o.Description)}</description>\n");
        foreach (var e in entries)
        {
            var url = Abs(o, $"/blog/{e.Slug}");
            sb.Append("<item>\n");
            sb.Append($"<title>{X(e.Title)}</title>\n<link>{X(url)}</link>\n<guid>{X(url)}</guid>\n");
            sb.Append($"<pubDate>{e.PublishedAt.ToString("r")}</pubDate>\n<description>{X(e.Summary)}</description>\n");
            sb.Append("</item>\n");
        }
        sb.Append("</channel></rss>");
        return sb.ToString();
    }
}
