using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ContentPlatform.Editorial.Application;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Kaynak makale sayfasından OKUNUR METNİ çıkarır (kaynak kodu DEĞİL) — AI'a temiz makale gider.
/// Amaç: kaliteli girdi + token tasarrufu. Yöntem:
///  1) script/style/nav/menü/yorum blokları atılır,
///  2) &lt;article&gt; → &lt;main&gt; → &lt;body&gt; önceliğiyle içerik kapsayıcısı seçilir,
///  3) p / h2 / h3 / li / blockquote metinleri toplanır; kısa menü kırıntıları elenir,
///  4) sonuç ~8000 karakterle sınırlanır (paragraf sınırında kesilir).
/// Başarısızlıkta boş döner — çağıran RSS özetine (RawInput) düşer, akış asla kırılmaz.
/// </summary>
internal sealed class ArticleTextExtractor(IHttpClientFactory httpClientFactory, ILogger<ArticleTextExtractor> logger)
    : IArticleTextExtractor
{
    public const string HttpClientName = "article-extract";
    private const int MaxChars = 8000;          // AI'a gidecek üst sınır (≈ token bütçesi)
    private const int MaxDownloadBytes = 2_000_000;

    public async Task<string?> ExtractAsync(string url, CancellationToken ct)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return null;

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            req.Headers.TryAddWithoutValidation("Accept-Language", "tr,en;q=0.8");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) { logger.LogInformation("Makale indirilemedi ({Code}): {Url}", (int)resp.StatusCode, url); return null; }

            var media = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (media.Length > 0 && !media.Contains("html", StringComparison.OrdinalIgnoreCase)) return null;

            var bytes = await ReadLimitedAsync(resp, ct);
            var html = DecodeHtml(bytes, resp.Content.Headers.ContentType?.CharSet);
            var text = ExtractReadable(html);
            if (string.IsNullOrWhiteSpace(text) || text.Length < 400)
            {
                logger.LogInformation("Makale metni yetersiz ({Len} kr): {Url}", text?.Length ?? 0, url);
                return null;
            }
            return text;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Makale çıkarma başarısız: {Url}", url);
            return null;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[16384];
        int read, total = 0;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxDownloadBytes) break; // dev sayfaları sonuna kadar indirme
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private static string DecodeHtml(byte[] bytes, string? charset)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(charset))
                return Encoding.GetEncoding(charset.Trim('"', '\'')).GetString(bytes);
        }
        catch { /* bilinmeyen charset → UTF-8 */ }
        var utf8 = Encoding.UTF8.GetString(bytes);
        // Meta charset başka bir kodlama diyorsa ve UTF-8 bozuksa yine de UTF-8 kalır (çoğu modern site UTF-8).
        return utf8;
    }

    /// <summary>HTML → okunur makale metni. Paragraflar boş satırla, ara başlıklar "## " ile ayrılır.</summary>
    internal static string ExtractReadable(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        // 1) Gürültü bloklarını tamamen at
        html = Regex.Replace(html, @"<!--.*?-->", " ", RegexOptions.Singleline);
        foreach (var tag in new[] { "script", "style", "noscript", "svg", "iframe", "form", "template", "figure" })
            html = Regex.Replace(html, $@"<{tag}\b.*?</{tag}\s*>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 2) İçerik kapsayıcısı: article → main → body (en uzun article bloğu seçilir)
        var container = LongestMatch(html, @"<article\b.*?</article\s*>")
                        ?? LongestMatch(html, @"<main\b.*?</main\s*>")
                        ?? LongestMatch(html, @"<body\b.*?</body\s*>")
                        ?? html;

        // 3) Kapsayıcı içindeki site iskeletini at
        foreach (var tag in new[] { "header", "footer", "nav", "aside" })
            container = Regex.Replace(container, $@"<{tag}\b.*?</{tag}\s*>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 4) Blok blok metin topla (p / h2 / h3 / li / blockquote)
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(container,
                     @"<(?<tag>p|h2|h3|li|blockquote)\b[^>]*>(?<inner>.*?)</\k<tag>\s*>",
                     RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var tag = m.Groups["tag"].Value.ToLowerInvariant();
            var text = CleanInline(m.Groups["inner"].Value);
            if (text.Length == 0) continue;

            // Menü/etiket kırıntılarını ele: başlık değilse en az 6 kelime iste
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (tag is "h2" or "h3")
            {
                if (words is < 1 or > 20) continue;
                sb.Append("\n\n## ").Append(text);
            }
            else
            {
                if (words < 6) continue;
                sb.Append("\n\n").Append(text);
            }
            if (sb.Length > MaxChars + 1000) break; // fazlasını toplama
        }

        var result = sb.ToString().Trim();

        // 5) Blok bulunamadıysa kaba düşüş: tüm etiketleri sök
        if (result.Length < 400)
        {
            var stripped = CleanInline(container);
            result = stripped.Length >= 400 ? stripped : result;
        }

        // 6) Üst sınır — paragraf sınırında kes
        if (result.Length > MaxChars)
        {
            var cut = result.LastIndexOf("\n\n", MaxChars, StringComparison.Ordinal);
            result = result[..(cut > MaxChars / 2 ? cut : MaxChars)].TrimEnd() + "\n\n[...devamı kısaltıldı]";
        }
        return result;
    }

    /// <summary>İç etiketleri söker, HTML varlıklarını çözer, boşlukları normalize eder.</summary>
    private static string CleanInline(string html)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private static string? LongestMatch(string html, string pattern)
    {
        string? best = null;
        foreach (Match m in Regex.Matches(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase))
            if (best is null || m.Value.Length > best.Length) best = m.Value;
        return best;
    }
}
