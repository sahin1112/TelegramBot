using System.Globalization;
using System.Xml.Linq;
using ContentPlatform.Ingestion.Application;
using ContentPlatform.Ingestion.Domain;

namespace ContentPlatform.Ingestion.Infrastructure;

/// <summary>RSS 2.0 ve Atom feed'lerini System.Xml.Linq ile okur (ek paket gerekmez).</summary>
internal sealed class RssFeedReader(IHttpClientFactory httpClientFactory) : IFeedReader
{
    public const string HttpClientName = "rss";
    public bool CanRead(SourceType type) => type == SourceType.Rss;

    public async Task<IReadOnlyList<RawFeedItem>> ReadAsync(Source source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source.Url)) return Array.Empty<RawFeedItem>();

        var client = httpClientFactory.CreateClient(HttpClientName);
        await using var stream = await client.GetStreamAsync(source.Url, ct);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

        XNamespace atom = "http://www.w3.org/2005/Atom";
        var items = new List<RawFeedItem>();

        // RSS 2.0: channel/item
        foreach (var item in doc.Descendants("item"))
        {
            var title = (string?)item.Element("title") ?? "";
            if (string.IsNullOrWhiteSpace(title)) continue;
            items.Add(new RawFeedItem(
                title.Trim(),
                (string?)item.Element("link"),
                (string?)item.Element("description"),
                ParseDate((string?)item.Element("pubDate")),
                (string?)item.Element("author")));
        }

        // Atom: entry
        foreach (var entry in doc.Descendants(atom + "entry"))
        {
            var title = (string?)entry.Element(atom + "title") ?? "";
            if (string.IsNullOrWhiteSpace(title)) continue;
            var link = entry.Elements(atom + "link").FirstOrDefault()?.Attribute("href")?.Value;
            items.Add(new RawFeedItem(
                title.Trim(), link,
                (string?)entry.Element(atom + "summary") ?? (string?)entry.Element(atom + "content"),
                ParseDate((string?)entry.Element(atom + "updated")),
                (string?)entry.Element(atom + "author")?.Element(atom + "name")));
        }

        return items;
    }

    private static DateTimeOffset? ParseDate(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
