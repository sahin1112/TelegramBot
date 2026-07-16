using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using ContentPlatform.Ingestion.Application;
using ContentPlatform.Ingestion.Domain;

namespace ContentPlatform.Ingestion.Infrastructure;

/// <summary>
/// Sayfa (WebPage) kaynağını okur. CSS `Selector` verilmişse listeleme sayfasından makale
/// linklerini çıkarır (her biri bir öğe); verilmemişse sayfayı TEK öğe (başlık + açıklama) sayar.
/// HTTP çekme SSRF sertleştirmeli (webpage HttpClient'ı SsrfGuard ConnectCallback ile kurulur).
/// Ayrıştırma AngleSharp ile (gerçek CSS selector desteği).
/// </summary>
internal sealed class WebPageFeedReader(IHttpClientFactory httpClientFactory) : IFeedReader
{
    public const string HttpClientName = "webpage";
    private const int MaxBytes = 4_000_000;
    private const int MaxItems = 50;

    public bool CanRead(SourceType type) => type == SourceType.WebPage;

    public async Task<IReadOnlyList<RawFeedItem>> ReadAsync(Source source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source.Url)) return Array.Empty<RawFeedItem>();
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Array.Empty<RawFeedItem>();

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var resp = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        // MIME allowlist: yalnız HTML/XHTML.
        var mediaType = resp.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
            !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<RawFeedItem>();

        var html = await ReadCappedAsync(resp, ct);

        var context = BrowsingContext.New(Configuration.Default);
        using var doc = await context.OpenAsync(r => r.Content(html).Address(source.Url), ct);

        var items = new List<RawFeedItem>();

        if (!string.IsNullOrWhiteSpace(source.Selector))
        {
            IEnumerable<IElement> matched;
            try { matched = doc.QuerySelectorAll(source.Selector); }
            catch { return Array.Empty<RawFeedItem>(); } // geçersiz CSS selector

            foreach (var el in matched)
            {
                if (items.Count >= MaxItems) break;
                var anchor = (el as IHtmlAnchorElement) ?? el.QuerySelector("a") as IHtmlAnchorElement;
                var href = anchor?.Href;                              // AngleSharp mutlak URL'e çözer
                var title = Clean(anchor?.TextContent ?? el.TextContent);
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href)) continue;
                if (!IsHttp(href)) continue;
                items.Add(new RawFeedItem(title!, href, null, null, null));
            }
        }
        else
        {
            var title = Clean(doc.Title);
            if (string.IsNullOrWhiteSpace(title)) title = Clean(doc.QuerySelector("h1")?.TextContent);
            var desc = (doc.QuerySelector("meta[name='description']") as IHtmlMetaElement)?.Content
                       ?? doc.QuerySelector("meta[property='og:description']")?.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(title))
                items.Add(new RawFeedItem(title!, source.Url, Clean(desc), null, null));
        }

        return items
            .GroupBy(i => i.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static bool IsHttp(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private static string? Clean(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : Regex.Replace(s, @"\s+", " ").Trim();

    private static async Task<string> ReadCappedAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            var allowed = Math.Min(read, MaxBytes - (int)ms.Length);
            if (allowed <= 0) break;
            ms.Write(buffer, 0, allowed);
            if (ms.Length >= MaxBytes) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
