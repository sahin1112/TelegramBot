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
    IReadOnlyList<string> Tags, string? MediaUrl, DateTimeOffset CreatedAt, DateTimeOffset? ScheduledAt, DateTimeOffset? PublishedAt);

public sealed record PagedContentDto(IReadOnlyList<ContentSummaryDto> Items, int Page, int Size, int Total);

public sealed record EditRevisionRequest(
    string Title, string ShortX, string BodyHtml, string? InstagramCaption,
    IReadOnlyList<string> Tags, string? PrimaryKeyword, string? ImageAltText);

public sealed record TestModeRequest(bool Enabled);
