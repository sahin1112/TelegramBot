using ContentPlatform.Platform.Domain;

namespace ContentPlatform.Platform.Application;

public interface ISettingsRepository
{
    Task<SystemSetting?> GetAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken ct);
    Task AddAsync(SystemSetting setting, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
