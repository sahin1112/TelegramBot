namespace ContentPlatform.Abstractions;

/// <summary>Kategori yayın kadansı. Immediate = onaylanınca hemen; Interval = her N dakikada bir; DailySlots = gün içi sabit saatler.</summary>
public enum ScheduleMode { Immediate, Interval, DailySlots }

/// <summary>
/// Bir kategorinin yayın zamanlama politikası.
/// <paramref name="PostsPerDay"/> = günlük tavan (0 = sınırsız). <paramref name="DailyTimes"/> = "HH:mm" listesi (DailySlots).
/// </summary>
public sealed record SchedulePolicy(
    ScheduleMode Mode,
    int PostsPerDay,
    IReadOnlyList<string> DailyTimes,
    int IntervalMinutes,
    string TimeZoneId);

/// <summary>Kategori bazlı zamanlama politikasını verir (Platform uygular). null → politika yok = hemen.</summary>
public interface ISchedulePolicyProvider
{
    Task<SchedulePolicy?> GetForCategoryAsync(Guid? categoryId, CancellationToken ct);
}

/// <summary>Bir içerik için bir sonraki uygun yayın anını hesaplar. null → hemen yayınla.</summary>
public interface ISchedulePlanner
{
    Task<DateTimeOffset?> NextSlotAsync(Guid? categoryId, CancellationToken ct);
}

/// <summary>
/// Saf (yan etkisiz) slot hesaplayıcı — politikaya ve mevcut planlı zamanlara göre bir sonraki anı verir.
/// Test edilebilir olması için BCL dışında bağımlılığı yoktur.
/// </summary>
public static class SlotCalculator
{
    public static DateTimeOffset? Next(SchedulePolicy policy, DateTimeOffset nowUtc, IReadOnlyList<DateTimeOffset> existingUtc)
    {
        if (policy.Mode == ScheduleMode.Immediate) return null;

        var tz = ResolveTz(policy.TimeZoneId);
        var nowWall = TimeZoneInfo.ConvertTimeFromUtc(nowUtc.UtcDateTime, tz); // DateTimeKind.Unspecified
        var existingWall = existingUtc
            .Select(x => TimeZoneInfo.ConvertTimeFromUtc(x.UtcDateTime, tz))
            .ToList();

        if (policy.Mode == ScheduleMode.Interval)
        {
            var interval = policy.IntervalMinutes <= 0 ? 60 : policy.IntervalMinutes;
            var cand = nowWall;
            if (existingWall.Count > 0)
            {
                var after = existingWall.Max().AddMinutes(interval);
                if (after > cand) cand = after;
            }
            // Günlük tavan: o gün dolduysa ertesi günün başına kaydır.
            if (policy.PostsPerDay > 0)
            {
                var guard = 0;
                while (existingWall.Count(x => x.Date == cand.Date) >= policy.PostsPerDay && guard++ < 366)
                    cand = cand.Date.AddDays(1);
            }
            return ToUtc(cand, tz);
        }

        // DailySlots
        var times = ParseTimes(policy.DailyTimes);
        if (times.Count == 0) return null; // saat tanımlı değil → hemen
        var cap = policy.PostsPerDay <= 0 ? times.Count : Math.Min(policy.PostsPerDay, times.Count);

        for (var day = 0; day < 120; day++)
        {
            var date = nowWall.Date.AddDays(day);
            var used = existingWall.Count(x => x.Date == date);
            foreach (var t in times)
            {
                var slot = date + t;
                if (slot <= nowWall) continue;
                if (used >= cap) break;
                if (existingWall.Any(x => x == slot)) continue;
                return ToUtc(slot, tz);
            }
        }
        return null;
    }

    private static DateTimeOffset ToUtc(DateTime wall, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    private static List<TimeSpan> ParseTimes(IReadOnlyList<string> raw)
    {
        var list = new List<TimeSpan>();
        foreach (var s in raw)
        {
            if (TimeSpan.TryParse(s?.Trim(), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                list.Add(t);
        }
        return list.Distinct().OrderBy(x => x).ToList();
    }

    private static TimeZoneInfo ResolveTz(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }
}
