using ContentPlatform.Abstractions;

namespace ContentPlatform.Ingestion.Contracts;

/// <summary>Yeni bir ham içerik keşfedildi (dedup geçti). Editorial bunu taslak olarak alır.</summary>
public sealed record ContentDiscoveredIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid? CategoryId,
    string SourceKind,
    string? SourceUrl,
    string SourceHash,
    string RawTitle,
    string? RawSummary,
    string RawInput,
    FactPack FactPack,
    // Kaynak bazlı otomasyon (null = kategoriden devral)
    bool? SrcAutoContent = null,
    bool? SrcAutoImage = null,
    bool? SrcAutoVideo = null,
    // Kaynak bazlı görsel havuzu override (boş/null = kategoriden devral)
    string? SrcCard1x1 = null,
    string? SrcCardReels = null) : IIntegrationEvent;
