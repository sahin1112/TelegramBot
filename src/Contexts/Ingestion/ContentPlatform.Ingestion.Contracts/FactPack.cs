namespace ContentPlatform.Ingestion.Contracts;

/// <summary>
/// Kaynak & olgu katmanı (MVP hafif sürüm): AI'a ham metin yerine bu verilir.
/// Kaynak kaybolsa bile neye dayanıldığı bilinir; atıf ve doğruluk denetlenebilir.
/// </summary>
public sealed record FactPack(
    string? SourceUrl,
    string SourceTitle,
    string? Publisher,
    DateTimeOffset? PublishedAt,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<string> Claims);
