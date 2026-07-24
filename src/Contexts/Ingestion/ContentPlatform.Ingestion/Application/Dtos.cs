using ContentPlatform.Ingestion.Domain;

namespace ContentPlatform.Ingestion.Application;

public sealed record CreateSourceRequest(Guid? CategoryId, SourceType Type, string? Url, int PollIntervalMinutes, string? Selector, DateTimeOffset? IngestSince,
    bool? AutoContent = null, bool? AutoImage = null, bool? AutoVideo = null, string? Card1x1 = null, string? CardReels = null);
public sealed record SourceDto(Guid Id, Guid? CategoryId, SourceType Type, string? Url, int PollIntervalMinutes, bool IsActive, DateTimeOffset? LastPolledAt, DateTimeOffset? IngestSince,
    bool? AutoContent, bool? AutoImage, bool? AutoVideo, string? Card1x1, string? CardReels);

public sealed record UpdateSourceRequest(Guid? CategoryId, string? Url, int PollIntervalMinutes, string? Selector, DateTimeOffset? IngestSince,
    bool? AutoContent = null, bool? AutoImage = null, bool? AutoVideo = null, string? Card1x1 = null, string? CardReels = null);
public sealed record TestSourceRequest(SourceType Type, string? Url, string? Selector);
