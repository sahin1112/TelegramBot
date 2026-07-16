using ContentPlatform.Ingestion.Domain;

namespace ContentPlatform.Ingestion.Application;

public sealed record CreateSourceRequest(Guid? CategoryId, SourceType Type, string? Url, int PollIntervalMinutes, string? Selector);
public sealed record SourceDto(Guid Id, Guid? CategoryId, SourceType Type, string? Url, int PollIntervalMinutes, bool IsActive, DateTimeOffset? LastPolledAt);

public sealed record UpdateSourceRequest(Guid? CategoryId, string? Url, int PollIntervalMinutes, string? Selector);
public sealed record TestSourceRequest(SourceType Type, string? Url, string? Selector);
