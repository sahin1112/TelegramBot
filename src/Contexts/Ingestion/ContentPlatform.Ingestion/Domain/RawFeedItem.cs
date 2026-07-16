namespace ContentPlatform.Ingestion.Domain;

/// <summary>Kaynaktan çıkarılan ham öğe (entity değil, geçici).</summary>
public sealed record RawFeedItem(string Title, string? Url, string? Summary, DateTimeOffset? PublishedAt, string? Author);
