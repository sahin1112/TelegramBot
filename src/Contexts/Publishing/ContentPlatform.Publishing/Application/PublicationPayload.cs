namespace ContentPlatform.Publishing.Application;

/// <summary>Yayın anlık kopyası — retry AI'yı tekrar çalıştırmadan aynı içeriği gönderebilsin.</summary>
public sealed record PublicationPayload(
    string? Title, string Text, IReadOnlyList<string> Hashtags, string? MediaUrl, string? Link);

public sealed record PublicationDto(
    Guid Id, Guid ContentItemId, string Channel, string TargetRef, string Status,
    string? ExternalId, int Attempts, string? Error, DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledAt, DateTimeOffset? PublishedAt);

public sealed record RescheduleRequest(DateTimeOffset? ScheduledAt);
