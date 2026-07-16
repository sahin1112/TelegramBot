using ContentPlatform.Platform.Application;
using ContentPlatform.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Infrastructure;

internal sealed class SocialAccountRepository(PlatformDbContext db) : ISocialAccountRepository
{
    public Task<SocialAccount?> GetAsync(Guid id, CancellationToken ct) =>
        db.SocialAccounts.Include(x => x.Targets).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<PublicationTarget?> GetTargetAsync(Guid targetId, CancellationToken ct) =>
        db.PublicationTargets.FirstOrDefaultAsync(x => x.Id == targetId, ct);

    public async Task AddAsync(SocialAccount account, CancellationToken ct) =>
        await db.SocialAccounts.AddAsync(account, ct);

    public async Task<IReadOnlyList<SocialAccount>> ListAsync(CancellationToken ct) =>
        await db.SocialAccounts.Include(x => x.Targets).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<SocialAccount>> ListByPlatformAsync(PlatformKind platform, CancellationToken ct) =>
        await db.SocialAccounts.Where(x => x.Platform == platform).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class SettingsRepository(PlatformDbContext db) : ContentPlatform.Platform.Application.ISettingsRepository
{
    public Task<ContentPlatform.Platform.Domain.SystemSetting?> GetAsync(string key, CancellationToken ct) =>
        db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
    public async Task<IReadOnlyList<ContentPlatform.Platform.Domain.SystemSetting>> ListAsync(CancellationToken ct) =>
        await db.SystemSettings.OrderBy(x => x.Key).ToListAsync(ct);
    public async Task AddAsync(ContentPlatform.Platform.Domain.SystemSetting setting, CancellationToken ct) =>
        await db.SystemSettings.AddAsync(setting, ct);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
