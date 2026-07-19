using ContentPlatform.Editorial.Domain;

namespace ContentPlatform.Editorial.Application;

public sealed record ContentSummaryDto(
    Guid Id,
    ContentOrigin Origin,
    EditorialStatus EditorialStatus,
    MediaStatus MediaStatus,
    RiskLevel RiskLevel,
    string? Title,
    DateTimeOffset CreatedAt);

public sealed record ApproveRequest(ImageSource? ImageSource, bool? TestMode, DateTimeOffset? ScheduledAt);
public sealed record BulkApproveRequest(IReadOnlyList<Guid> Ids);

public sealed record AddManualAiRequest(
    string Title, string RawInput, Guid? CategoryId, ImageSource ImageSource, bool TestMode, DateTimeOffset? ScheduledAt);

public sealed record AddManualNoAiRequest(
    string Title, string ShortX, string BodyHtml, string? InstagramCaption,
    IReadOnlyList<string> Tags, string? PrimaryKeyword, string? ImageAltText,
    Guid? CategoryId, ImageSource ImageSource, bool TestMode, DateTimeOffset? ScheduledAt);

public sealed record ContentDetailDto(
    Guid Id, ContentOrigin Origin, EditorialStatus EditorialStatus, MediaStatus MediaStatus,
    RiskLevel RiskLevel, ImageSource ImageSource, bool TestMode, Guid? CategoryId,
    string? Title, string? ShortX, string? BodyHtml, string? InstagramCaption,
    IReadOnlyList<string> Tags, string? MediaUrl, string? VideoUrl, DateTimeOffset CreatedAt, DateTimeOffset? ScheduledAt, DateTimeOffset? PublishedAt,
    string? HoldReason, string? RawInput);

public sealed record PagedContentDto(IReadOnlyList<ContentSummaryDto> Items, int Page, int Size, int Total);

public sealed record AuditDto(AuditEvent Event, ActorType ActorType, string ActorRef, string? Detail, DateTimeOffset At);

public sealed record EditRevisionRequest(
    string Title, string ShortX, string BodyHtml, string? InstagramCaption,
    IReadOnlyList<string> Tags, string? PrimaryKeyword, string? ImageAltText);

public sealed record TestModeRequest(bool Enabled);

/// <summary>Panelden "tüm alanları AI ile üret" — seed boşsa kayıtlı ham metin kullanılır.</summary>
public sealed record GenerateDraftRequest(string? SeedInput);

/// <summary>Tek alan üretimi — verilen bağlamdan istenen alanı üretir.</summary>
public sealed record GenerateFieldRequest(string Field, string? Title, string? ShortX, string? BodyHtml, string? InstagramCaption, string? RawInput);

/// <summary>Detay ekranından görsel üretimi — hangi kaynak (Ai/SkiaCard/Manual).
/// CardStyle: SkiaCard şablon indeksi (0..23); null = RASTGELE (her basışta farklı tasarım).</summary>
public sealed record PreviewImageRequest(ImageSource ImageSource, int? CardStyle = null);

/// <summary>Slayt videosu üretim isteği. Style: 0..19 şablon indeksi; null = RASTGELE.</summary>
public sealed record PreviewVideoRequest(int? Style = null);
