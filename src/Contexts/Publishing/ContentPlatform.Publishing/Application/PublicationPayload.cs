namespace ContentPlatform.Publishing.Application;

/// <summary>Yayın anlık kopyası — retry AI'yı tekrar çalıştırmadan aynı içeriği gönderebilsin.</summary>
public sealed record PublicationPayload(
    string? Title, string Text, IReadOnlyList<string> Hashtags, string? MediaUrl, string? Link,
    string? ButtonUrl = null, string? ButtonText = null, string? VideoUrl = null,
    string? IgCaption = null); // Instagram'a özel uzun açıklama (varsa ShortX yerine bu gider)

public sealed record PublicationDto(
    Guid Id, Guid ContentItemId, Guid? CategoryId, string Channel, string TargetRef, string Status,
    string? ExternalId, int Attempts, string? Error, DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledAt, DateTimeOffset? PublishedAt, string? Title);

public sealed record RescheduleRequest(DateTimeOffset? ScheduledAt);
