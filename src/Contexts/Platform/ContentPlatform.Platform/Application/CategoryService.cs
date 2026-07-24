using ContentPlatform.Platform.Domain;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Platform.Application;

public sealed record CategoryDto(Guid Id, string Name, string Slug, string? Language,
    string DefaultImageSource, int AdEveryNPosts, bool RssAutoApprove, bool IsActive,
    string ScheduleMode, int PostsPerDay, string DailyTimes, int IntervalMinutes, string TimeZoneId,
    bool AutoContent, bool AutoImage, bool AutoVideo, bool AutoPublish,
    string Card1x1, string CardReels, bool AttentionBadges);
public sealed record CreateCategoryRequest(string Name, string Slug, string? Language,
    string DefaultImageSource, int AdEveryNPosts, bool RssAutoApprove,
    string? ScheduleMode, int? PostsPerDay, string? DailyTimes, int? IntervalMinutes, string? TimeZoneId,
    bool AutoContent = false, bool AutoImage = false, bool AutoVideo = false, bool AutoPublish = false,
    string? Card1x1 = null, string? CardReels = null, bool AttentionBadges = false);
public sealed record UpdateCategoryRequest(string Name, string Slug, string? Language,
    string DefaultImageSource, int AdEveryNPosts, bool RssAutoApprove,
    string? ScheduleMode, int? PostsPerDay, string? DailyTimes, int? IntervalMinutes, string? TimeZoneId,
    bool AutoContent = false, bool AutoImage = false, bool AutoVideo = false, bool AutoPublish = false,
    string? Card1x1 = null, string? CardReels = null, bool AttentionBadges = false);

public sealed class CategoryService(ICategoryRepository repository, IClock clock)
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct) =>
        (await repository.ListAsync(ct)).Select(Dto).ToList();

    public async Task<Result<Guid>> CreateAsync(CreateCategoryRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return Result.Failure<Guid>(Error.Validation("Ad gerekli."));
        var slug = string.IsNullOrWhiteSpace(r.Slug) ? Slugify(r.Name) : Slugify(r.Slug);
        var c = new Category(r.Name.Trim(), slug, r.Language, r.DefaultImageSource, r.AdEveryNPosts, r.RssAutoApprove,
            r.AutoContent, r.AutoImage, r.AutoVideo, r.AutoPublish,
            r.Card1x1 ?? "", r.CardReels ?? "", r.AttentionBadges, clock);
        c.SetSchedule(r.ScheduleMode ?? "Immediate", r.PostsPerDay ?? 0, r.DailyTimes, r.IntervalMinutes ?? 0, r.TimeZoneId, clock);
        await repository.AddAsync(c, ct);
        await repository.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, UpdateCategoryRequest r, CancellationToken ct)
    {
        var c = await repository.GetAsync(id, ct);
        if (c is null) return Result.Failure(Error.NotFound("Kategori"));
        c.Update(r.Name.Trim(), Slugify(r.Slug), r.Language, r.DefaultImageSource, r.AdEveryNPosts, r.RssAutoApprove,
            r.AutoContent, r.AutoImage, r.AutoVideo, r.AutoPublish,
            r.Card1x1 ?? "", r.CardReels ?? "", r.AttentionBadges, clock);
        c.SetSchedule(r.ScheduleMode ?? "Immediate", r.PostsPerDay ?? 0, r.DailyTimes, r.IntervalMinutes ?? 0, r.TimeZoneId, clock);
        await repository.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ToggleAsync(Guid id, CancellationToken ct)
    {
        var c = await repository.GetAsync(id, ct);
        if (c is null) return Result.Failure(Error.NotFound("Kategori"));
        c.Toggle(clock); await repository.SaveChangesAsync(ct); return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var c = await repository.GetAsync(id, ct);
        if (c is null) return Result.Failure(Error.NotFound("Kategori"));
        repository.Remove(c); await repository.SaveChangesAsync(ct); return Result.Success();
    }

    private static CategoryDto Dto(Category c) => new(c.Id, c.Name, c.Slug, c.Language, c.DefaultImageSource, c.AdEveryNPosts, c.RssAutoApprove, c.IsActive,
        c.ScheduleMode, c.PostsPerDay, c.DailyTimes, c.IntervalMinutes, c.TimeZoneId,
        c.AutoContent, c.AutoImage, c.AutoVideo, c.AutoPublish,
        c.Card1x1, c.CardReels, c.AttentionBadges);

    private static string Slugify(string s) => new string(
        s.Trim().ToLowerInvariant().Replace('ı','i').Replace('ğ','g').Replace('ü','u').Replace('ş','s').Replace('ö','o').Replace('ç','c')
         .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray())
        .Trim('-');
}
