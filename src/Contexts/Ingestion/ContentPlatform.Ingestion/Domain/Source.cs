using ContentPlatform.SharedKernel;

namespace ContentPlatform.Ingestion.Domain;

/// <summary>Bir içerik kaynağı. RSS/sayfa periyodik taranır; Manual/TelegramAdmin taranmaz (dışarıdan gelir).</summary>
public sealed class Source : Entity
{
    private Source() { } // EF

    public Source(Guid? categoryId, SourceType type, string? url, int pollIntervalMinutes, string? selector, IClock clock)
    {
        CategoryId = categoryId;
        Type = type;
        Url = url;
        PollIntervalMinutes = pollIntervalMinutes < 1 ? 15 : pollIntervalMinutes;
        Selector = selector;
        IsActive = true;
        CreatedAt = clock.UtcNow;
    }

    public Guid? CategoryId { get; private set; }
    public SourceType Type { get; private set; }
    public string? Url { get; private set; }
    public int PollIntervalMinutes { get; private set; }
    public string? Selector { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastPolledAt { get; private set; }
    public string? LastItemHash { get; private set; }

    public bool IsPollable => Type is SourceType.Rss or SourceType.WebPage;

    public bool IsDue(DateTimeOffset now) =>
        IsActive && IsPollable &&
        (LastPolledAt is null || now - LastPolledAt >= TimeSpan.FromMinutes(PollIntervalMinutes));

    public void MarkPolled(string? lastItemHash, IClock clock)
    {
        LastPolledAt = clock.UtcNow;
        if (lastItemHash is not null) LastItemHash = lastItemHash;
        Touch(clock);
    }

    public void Enable(IClock clock) { IsActive = true; Touch(clock); }
    public void Disable(IClock clock) { IsActive = false; Touch(clock); }

    public void Update(Guid? categoryId, string? url, int pollIntervalMinutes, string? selector, IClock clock)
    {
        CategoryId = categoryId;
        Url = url;
        PollIntervalMinutes = pollIntervalMinutes < 1 ? 15 : pollIntervalMinutes;
        Selector = selector;
        Touch(clock);
    }
}
