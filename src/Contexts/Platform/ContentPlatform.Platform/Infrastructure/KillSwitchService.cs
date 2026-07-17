using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Application;
using ContentPlatform.Platform.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.EntityFrameworkCore;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>
/// Kill-switch okuma (IKillSwitch) + yönetim (IKillSwitchAdmin). DB'den okur (süreçler arası paylaşım);
/// bir işlem (scope) içinde "engaged" kümesi bir kez çekilip yeniden kullanılır (aynı yayın döngüsündeki N kontrol için).
/// </summary>
internal sealed class KillSwitchService(PlatformDbContext db, IClock clock) : IKillSwitch, IKillSwitchAdmin
{
    private List<KillSwitch>? _engaged;

    private async Task<List<KillSwitch>> EngagedAsync(CancellationToken ct) =>
        _engaged ??= await db.KillSwitches.AsNoTracking().Where(k => k.Engaged).ToListAsync(ct);

    private static bool Match(KillSwitch k, KillScope scope, string? key) =>
        k.Scope == scope && (key == null ? k.Key == null : k.Key == key);

    public async Task<bool> IsAiStoppedAsync(Guid? categoryId, CancellationToken ct)
    {
        var e = await EngagedAsync(ct);
        return e.Any(k => Match(k, KillScope.Global, null) || Match(k, KillScope.Ai, null)
                          || (categoryId is { } c && Match(k, KillScope.Category, c.ToString())));
    }

    public async Task<bool> IsIngestionStoppedAsync(Guid? categoryId, CancellationToken ct)
    {
        var e = await EngagedAsync(ct);
        return e.Any(k => Match(k, KillScope.Global, null) || Match(k, KillScope.Ingestion, null)
                          || (categoryId is { } c && Match(k, KillScope.Category, c.ToString())));
    }

    public async Task<bool> IsPublishingStoppedAsync(PlatformKind platform, Guid? categoryId, Guid socialAccountId, CancellationToken ct)
    {
        var e = await EngagedAsync(ct);
        return e.Any(k => Match(k, KillScope.Global, null)
                          || Match(k, KillScope.Publishing, null)
                          || Match(k, KillScope.Channel, platform.ToString())
                          || Match(k, KillScope.Account, socialAccountId.ToString())
                          || (categoryId is { } c && Match(k, KillScope.Category, c.ToString())));
    }

    public async Task<bool> IsAdsStoppedAsync(CancellationToken ct)
    {
        var e = await EngagedAsync(ct);
        return e.Any(k => Match(k, KillScope.Global, null) || Match(k, KillScope.Ads, null));
    }

    // ---- Yönetim ----
    public async Task<IReadOnlyList<KillSwitchDto>> ListAsync(CancellationToken ct) =>
        await db.KillSwitches.AsNoTracking().OrderBy(k => k.Scope).ThenBy(k => k.Key)
            .Select(k => new KillSwitchDto(k.Scope, k.Key, k.Engaged, k.Reason, k.UpdatedAt ?? k.CreatedAt))
            .ToListAsync(ct);

    public async Task SetAsync(KillScope scope, string? key, bool engaged, string? reason, CancellationToken ct)
    {
        var norm = string.IsNullOrWhiteSpace(key) ? null : key;
        var existing = norm is null
            ? await db.KillSwitches.FirstOrDefaultAsync(k => k.Scope == scope && k.Key == null, ct)
            : await db.KillSwitches.FirstOrDefaultAsync(k => k.Scope == scope && k.Key == norm, ct);

        if (existing is null)
            db.KillSwitches.Add(new KillSwitch(scope, norm, engaged, reason, clock));
        else
            existing.Set(engaged, reason, clock);

        await db.SaveChangesAsync(ct);
        _engaged = null; // scope-içi önbelleği geçersiz kıl
    }
}
