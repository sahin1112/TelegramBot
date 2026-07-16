using ContentPlatform.Abstractions;

namespace ContentPlatform.Ingestion.Contracts;

/// <summary>Yeni bir ham içerik keşfedildi (dedup geçti). Editorial bunu onay kuyruğuna alır.</summary>
public sealed record ContentDiscoveredIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid? CategoryId,
    string SourceKind,       // "Rss" | "WebPage" (Editorial ContentOrigin'e eşlenir)
    string? SourceUrl,
    string SourceHash,
    string RawTitle,
    string? RawSummary,
    string RawInput,
    FactPack FactPack) : IIntegrationEvent;
