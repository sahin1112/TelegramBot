using ContentPlatform.Ingestion.Application;
using ContentPlatform.Ingestion.Contracts;
using ContentPlatform.Ingestion.Domain;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Ingestion.Infrastructure;

/// <summary>MVP hafif FactPack: kaynak meta + özetin ilk cümlelerini iddia olarak çıkarır.</summary>
internal sealed class FactPackExtractor(IClock clock) : IFactPackExtractor
{
    public FactPack Extract(Source source, RawFeedItem item)
    {
        var text = item.Summary ?? item.Title;
        var claims = text
            .Split(new[] { ". ", "! ", "? ", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 20)
            .Take(3)
            .ToList();

        return new FactPack(
            item.Url, item.Title, item.Author, item.PublishedAt, clock.UtcNow,
            claims.Count > 0 ? claims : new List<string> { item.Title });
    }
}
