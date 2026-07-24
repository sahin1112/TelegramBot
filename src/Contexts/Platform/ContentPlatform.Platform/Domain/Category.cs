using ContentPlatform.SharedKernel;

namespace ContentPlatform.Platform.Domain;

/// <summary>
/// Kategori — sistemin kök konfigürasyon varlığı. Kaynaklar, hedefler ve içerikler ona asılır.
/// </summary>
public sealed class Category : Entity
{
    private Category() { }

    public Category(string name, string slug, string? language, string defaultImageSource,
        int adEveryNPosts, bool rssAutoApprove,
        bool autoContent, bool autoImage, bool autoVideo, bool autoPublish,
        string card1x1, string cardReels, bool attentionBadges, IClock clock)
    {
        Name = name;
        Slug = slug;
        Language = language;
        DefaultImageSource = defaultImageSource;
        AdEveryNPosts = adEveryNPosts < 1 ? 5 : adEveryNPosts;
        RssAutoApprove = rssAutoApprove;
        AutoContent = autoContent;
        AutoImage = autoImage;
        AutoVideo = autoVideo;
        AutoPublish = autoPublish;
        Card1x1 = card1x1 ?? "";
        CardReels = cardReels ?? "";
        AttentionBadges = attentionBadges;
        IsActive = true;
        ScheduleMode = "Immediate";
        PostsPerDay = 0;
        DailyTimes = "";
        IntervalMinutes = 0;
        TimeZoneId = "Europe/Istanbul";
        CreatedAt = clock.UtcNow;
    }

    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public string? Language { get; private set; }
    public string DefaultImageSource { get; private set; } = "SkiaCard"; // Ai | SkiaCard | Manual
    public int AdEveryNPosts { get; private set; }
    public bool RssAutoApprove { get; private set; }
    public bool IsActive { get; private set; }

    // ---- Otomatik üretim anahtarları ----
    public bool AutoContent { get; private set; }
    public bool AutoImage { get; private set; }
    public bool AutoVideo { get; private set; }
    public bool AutoPublish { get; private set; }

    // ---- Görsel şablon havuzu (virgülle ayrılmış dosya adları; boş = varsayılan SkiaCard) ----
    /// <summary>1:1 şablon dosya adları (wwwroot/assets/cards/1x1). "01.png,05.png" gibi.</summary>
    public string Card1x1 { get; private set; } = "";
    /// <summary>9:16 (reels/hikaye) şablon dosya adları (wwwroot/assets/cards/reels).</summary>
    public string CardReels { get; private set; } = "";
    /// <summary>Risk seviyesine göre SON DAKİKA rozeti bu kategoride kullanılsın mı?</summary>
    public bool AttentionBadges { get; private set; }

    // ---- Yayın kadansı ----
    public string ScheduleMode { get; private set; } = "Immediate";
    public int PostsPerDay { get; private set; }
    public string DailyTimes { get; private set; } = "";
    public int IntervalMinutes { get; private set; }
    public string TimeZoneId { get; private set; } = "Europe/Istanbul";

    public void Update(string name, string slug, string? language, string defaultImageSource,
        int adEveryNPosts, bool rssAutoApprove,
        bool autoContent, bool autoImage, bool autoVideo, bool autoPublish,
        string card1x1, string cardReels, bool attentionBadges, IClock clock)
    {
        Name = name; Slug = slug; Language = language; DefaultImageSource = defaultImageSource;
        AdEveryNPosts = adEveryNPosts < 1 ? 5 : adEveryNPosts; RssAutoApprove = rssAutoApprove;
        AutoContent = autoContent; AutoImage = autoImage; AutoVideo = autoVideo; AutoPublish = autoPublish;
        Card1x1 = card1x1 ?? ""; CardReels = cardReels ?? ""; AttentionBadges = attentionBadges;
        Touch(clock);
    }

    public void SetSchedule(string mode, int postsPerDay, string? dailyTimes, int intervalMinutes, string? timeZoneId, IClock clock)
    {
        ScheduleMode = mode is "Interval" or "DailySlots" ? mode : "Immediate";
        PostsPerDay = postsPerDay < 0 ? 0 : postsPerDay;
        DailyTimes = dailyTimes?.Trim() ?? "";
        IntervalMinutes = intervalMinutes < 0 ? 0 : intervalMinutes;
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? "Europe/Istanbul" : timeZoneId.Trim();
        Touch(clock);
    }

    public void Toggle(IClock clock) { IsActive = !IsActive; Touch(clock); }
}
