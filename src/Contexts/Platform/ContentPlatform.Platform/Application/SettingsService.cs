using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Domain;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Platform.Application;

/// <summary>Ayar okuma/yazma. Gizli değerler şifrelenir; listede maskelenir.</summary>
public sealed class SettingsService(ISettingsRepository repository, ICredentialProtector protector, IClock clock)
    : ISettingsProvider
{
    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var s = await repository.GetAsync(key, ct);
        if (s is null) return null;
        return s.IsSecret ? Safe(() => protector.Unprotect(s.Value)) : s.Value;
    }

    public async Task SetAsync(string key, string value, bool isSecret, CancellationToken ct)
    {
        var stored = isSecret ? protector.Protect(value) : value;
        var existing = await repository.GetAsync(key, ct);
        if (existing is null)
            await repository.AddAsync(new SystemSetting(key, stored, isSecret, clock), ct);
        else
            existing.Set(stored, isSecret, clock);
        await repository.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken ct)
    {
        var all = await repository.ListAsync(ct);
        return all.Select(s => new SettingDto(s.Key, s.IsSecret ? "••••••" : s.Value, s.IsSecret, s.UpdatedAt)).ToList();
    }

    private static string? Safe(Func<string> f) { try { return f(); } catch { return null; } }
}

public sealed record SettingDto(string Key, string MaskedValue, bool IsSecret, DateTimeOffset? UpdatedAt);
public sealed record SetSettingRequest(string Key, string Value, bool IsSecret);
