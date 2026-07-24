using ContentPlatform.SharedKernel;

namespace ContentPlatform.Ingestion.Domain;

/// <summary>Bir içerik kaynağı. RSS/sayfa periyodik taranır; Manual/TelegramAdmin taranmaz.</summary>
public sealed class Source : Entity
{
    private Source() { } // EF

    public Source(Guid? categoryId, SourceType type, string? url, int pollIntervalMinutes, string? selector, DateTimeOffset? ingestSince,
        bool? autoContent, bool? autoImage, bool? autoVideo, string? card1x1, string? cardReels, IClock clock)
    {
        CategoryId = categoryId;
        Type = type;
        Url = url;
        PollIntervalMinutes = pollIntervalMinutes < 1 ? 15 : pollIntervalMinutes;
        Selector = selector;
        IngestSince = ingestSince;
        AutoContent = autoContent;
        AutoImage = autoImage;
        AutoVideo = autoVideo;
        Card1x1 = card1x1;
        CardReels = cardReels;
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

    /// <summary>Bu tarih-saatten ÖNCE yayınlanan öğeler alınmaz (boş = tümü).</summary>
    public DateTimeOffset? IngestSince { get; private set; }

    // ---- Otomatik üretim (kaynak bazlı; null = KATEGORİDEN DEVRAL) ----
    public bool? AutoContent { get; private set; }
    public bool? AutoImage { get; private set; }
    public bool? AutoVideo { get; private set; }

    // ---- Görsel şablon havuzu override (boş/null = kategoriden devral) ----
    /// <summary>Bu kaynağa özel 1:1 şablon dosya adları (virgüllü). Boşsa kategorininki kullanılır.</summary>
    public string? Card1x1 { get; private set; }
    /// <summary>Bu kaynağa özel 9:16 şablon dosya adları (virgüllü). Boşsa kategorininki.</summary>
    public string? CardReels { get; private set; }

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

    public void Update(Guid? categoryId, string? url, int pollIntervalMinutes, string? selector, DateTimeOffset? ingestSince,
        bool? autoContent, bool? autoImage, bool? autoVideo, string? card1x1, string? cardReels, IClock clock)
    {
        CategoryId = categoryId;
        Url = url;
        PollIntervalMinutes = pollIntervalMinutes < 1 ? 15 : pollIntervalMinutes;
        Selector = selector;
        IngestSince = ingestSince;
        AutoContent = autoContent;
        AutoImage = autoImage;
        AutoVideo = autoVideo;
        Card1x1 = card1x1;
        CardReels = cardReels;
        Touch(clock);
    }
}
