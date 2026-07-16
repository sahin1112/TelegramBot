using ContentPlatform.SharedKernel;

namespace ContentPlatform.Platform.Domain;

/// <summary>Anahtar-değer ayar. Gizli değerler (API key/token) şifreli saklanır, panelde maskeli gösterilir.</summary>
public sealed class SystemSetting : Entity
{
    private SystemSetting() { }
    public SystemSetting(string key, string value, bool isSecret, IClock clock)
    {
        Key = key; Value = value; IsSecret = isSecret; CreatedAt = clock.UtcNow;
    }

    public string Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;
    public bool IsSecret { get; private set; }

    public void Set(string value, bool isSecret, IClock clock)
    {
        Value = value; IsSecret = isSecret; Touch(clock);
    }
}
