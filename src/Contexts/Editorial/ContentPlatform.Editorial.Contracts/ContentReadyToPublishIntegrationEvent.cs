using ContentPlatform.Abstractions;

namespace ContentPlatform.Editorial.Contracts;

/// <summary>İçerik üretildi ve yayına hazır (görsel dahil). Publishing bunu dağıtım planına alır.</summary>
public sealed record ContentReadyToPublishIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ContentItemId,
    Guid? CategoryId,
    bool TestMode,
    string Title,
    string ShortX,
    string BodyHtml,
    string? InstagramCaption,
    IReadOnlyList<string> Tags,
    string? PrimaryKeyword,
    string? MediaUrl,
    string? Link,
    DateTimeOffset? ScheduledAt,
    bool AdGate = false,
    string? VideoUrl = null) : IIntegrationEvent;
