using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Site.Domain;
using ContentPlatform.SharedKernel;
using Ganss.Xss;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Site.Application;

/// <summary>
/// İçerik yayına hazır olunca blog gönderisini üretir/günceller (idempotent).
/// Test modundaki içerik public bloga KONMAZ. AI/dışarıdan gelen HTML sanitize edilir (00 §20).
/// </summary>
public sealed class ContentReadyToPublishBlogHandler(
    IBlogRepository repository,
    IClock clock,
    ILogger<ContentReadyToPublishBlogHandler> logger)
    : IIntegrationEventHandler<ContentReadyToPublishIntegrationEvent>
{
    private static readonly HtmlSanitizer Sanitizer = BuildSanitizer();

    public async Task HandleAsync(ContentReadyToPublishIntegrationEvent e, CancellationToken ct)
    {
        if (e.TestMode) return; // test içeriği public bloga girmez

        var safeBody = Sanitizer.Sanitize(e.BodyHtml ?? "");
        var meta = BuildMeta(e.ShortX, safeBody);
        var slug = BlogSlug.Build(e.ContentItemId, e.PrimaryKeyword, e.Title);

        var existing = await repository.GetByContentItemAsync(e.ContentItemId, ct);
        if (existing is null)
        {
            var post = new BlogPost(e.ContentItemId, e.CategoryId, slug, e.Title, meta, safeBody,
                e.MediaUrl, e.Title, e.Tags, clock);
            await repository.AddAsync(post, ct);
            logger.LogInformation("Blog gönderisi oluşturuldu: {Slug}", slug);
        }
        else
        {
            existing.Update(e.Title, meta, safeBody, e.MediaUrl, e.Title, e.Tags, clock);
            logger.LogInformation("Blog gönderisi güncellendi: {Slug}", existing.Slug);
        }

        await repository.SaveChangesAsync(ct);
    }

    private static string BuildMeta(string? shortX, string bodyHtml)
    {
        var basis = !string.IsNullOrWhiteSpace(shortX) ? shortX! : StripTags(bodyHtml);
        basis = System.Text.RegularExpressions.Regex.Replace(basis, @"\s+", " ").Trim();
        return basis.Length <= 300 ? basis : basis[..297].TrimEnd() + "…";
    }

    private static string StripTags(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ");

    private static HtmlSanitizer BuildSanitizer()
    {
        var s = new HtmlSanitizer();
        // Blog gövdesi için güvenli, sade bir etiket kümesi.
        s.AllowedTags.Clear();
        foreach (var t in new[] { "p", "br", "h2", "h3", "h4", "ul", "ol", "li", "blockquote",
                                  "strong", "em", "b", "i", "a", "img", "figure", "figcaption",
                                  "pre", "code", "table", "thead", "tbody", "tr", "th", "td", "hr" })
            s.AllowedTags.Add(t);
        s.AllowedAttributes.Clear();
        foreach (var a in new[] { "href", "title", "src", "alt", "width", "height", "rel", "target" })
            s.AllowedAttributes.Add(a);
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("http");
        return s;
    }
}
