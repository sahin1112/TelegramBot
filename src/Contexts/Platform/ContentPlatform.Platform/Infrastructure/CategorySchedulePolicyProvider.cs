using ContentPlatform.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>Kategori kaydından yayın kadansı politikasını okur (Publishing bağlamı bunu port üzerinden çözer).</summary>
internal sealed class CategorySchedulePolicyProvider(PlatformDbContext db) : ISchedulePolicyProvider
{
    public async Task<SchedulePolicy?> GetForCategoryAsync(Guid? categoryId, CancellationToken ct)
    {
        if (categoryId is not { } id) return null;

        var c = await db.Categories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;

        var mode = Enum.TryParse<ScheduleMode>(c.ScheduleMode, ignoreCase: true, out var m) ? m : ScheduleMode.Immediate;
        var times = string.IsNullOrWhiteSpace(c.DailyTimes)
            ? Array.Empty<string>()
            : c.DailyTimes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new SchedulePolicy(
            mode,
            c.PostsPerDay,
            times,
            c.IntervalMinutes,
            string.IsNullOrWhiteSpace(c.TimeZoneId) ? "Europe/Istanbul" : c.TimeZoneId);
    }
}
