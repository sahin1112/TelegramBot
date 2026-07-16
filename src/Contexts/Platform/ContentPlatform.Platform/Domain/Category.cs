using ContentPlatform.SharedKernel;

namespace ContentPlatform.Platform.Domain;

/// <summary>
/// Kategori — sistemin kök konfigürasyon varlığı. Kaynaklar, hedefler ve içerikler ona asılır.
/// Görsel modu / risk gibi enum'lar bağlam bağımsızlığı için string tutulur.
/// </summary>
public sealed class Category : Entity
{
    private Category() { }

    public Category(string name, string slug, string? language, string defaultImageSource,
        int adEveryNPosts, bool rssAutoApprove, IClock clock)
    {
        Name = name;
        Slug = slug;
        Language = language;
        DefaultImageSource = defaultImageSource;
        AdEveryNPosts = adEveryNPosts < 1 ? 5 : adEveryNPosts;
        RssAutoApprove = rssAutoApprove;
        IsActive = true;
        // Zamanlama varsayılanı: hemen yayınla (İstanbul saatiyle).
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

    // ---- Yayın kadansı (kategori bazlı zamanlama) ----
    public string ScheduleMode { get; private set; } = "Immediate";   // Immediate | Interval | DailySlots
    public int PostsPerDay { get; private set; }                       // günlük tavan (0 = sınırsız)
    public string DailyTimes { get; private set; } = "";              // "09:00,12:00,18:00" (DailySlots)
    public int IntervalMinutes { get; private set; }                  // Interval modu
    public string TimeZoneId { get; private set; } = "Europe/Istanbul";

    public void Update(string name, string slug, string? language, string defaultImageSource,
        int adEveryNPosts, bool rssAutoApprove, IClock clock)
    {
        Name = name; Slug = slug; Language = language; DefaultImageSource = defaultImageSource;
        AdEveryNPosts = adEveryNPosts < 1 ? 5 : adEveryNPosts; RssAutoApprove = rssAutoApprove;
        Touch(clock);
    }

    /// <summary>Yayın kadansı politikasını günceller.</summary>
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
