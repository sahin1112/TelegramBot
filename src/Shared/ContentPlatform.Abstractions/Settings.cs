namespace ContentPlatform.Abstractions;

/// <summary>Global ayarları (API anahtarları, model, fiyat, kur) DB'den okur — çalışırken; yeniden deploy gerekmez.</summary>
public interface ISettingsProvider
{
    Task<string?> GetAsync(string key, CancellationToken ct);
}
