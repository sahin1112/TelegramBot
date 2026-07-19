using ContentPlatform.SharedKernel;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Domain;

/// <summary>
/// Bir sosyal platform hesabı. Kimlik bilgileri ŞİFRELİ saklanır (config değil, veri).
/// IG/Threads token'ları ~60 günde dolar; TokenExpiresAt + yenileme işi (TokenRefreshJob) izler.
/// </summary>
public sealed class SocialAccount : Entity
{
    private readonly List<PublicationTarget> _targets = new();

    private SocialAccount() { } // EF

    public SocialAccount(
        PlatformKind platform,
        string displayName,
        string credentialsEncrypted,
        DateTimeOffset? tokenExpiresAt,
        Guid? siteId,
        IClock clock)
    {
        Platform = platform;
        DisplayName = displayName;
        CredentialsEncrypted = credentialsEncrypted;
        TokenExpiresAt = tokenExpiresAt;
        SiteId = siteId;
        Status = AccountStatus.Active;
        CreatedAt = clock.UtcNow;
    }

    public PlatformKind Platform { get; private set; }
    public string DisplayName { get; private set; } = default!;
    public string CredentialsEncrypted { get; private set; } = default!;
    public DateTimeOffset? TokenExpiresAt { get; private set; }
    public Guid? SiteId { get; private set; }
    public AccountStatus Status { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }

    public IReadOnlyList<PublicationTarget> Targets => _targets;

    /// <summary>Süreli token kullanan platformlar yenilenir: IG/Threads (60 gün) + TikTok (24 saat).</summary>
    public bool UsesExpiringToken => Platform is PlatformKind.Instagram or PlatformKind.Threads or PlatformKind.TikTok;

    public bool NeedsRefresh(IClock clock, TimeSpan threshold) =>
        UsesExpiringToken && TokenExpiresAt is { } exp && exp - clock.UtcNow <= threshold;

    public void UpdateToken(string credentialsEncrypted, DateTimeOffset tokenExpiresAt, IClock clock)
    {
        CredentialsEncrypted = credentialsEncrypted;
        TokenExpiresAt = tokenExpiresAt;
        Status = AccountStatus.Active;
        LastError = null;
        LastCheckedAt = clock.UtcNow;
        Touch(clock);
    }

    public void MarkError(string error, IClock clock)
    {
        Status = AccountStatus.Error;
        LastError = error;
        LastCheckedAt = clock.UtcNow;
        Touch(clock);
    }

    public void Disable(IClock clock)
    {
        Status = AccountStatus.Disabled;
        Touch(clock);
    }

    public void MarkChecked(IClock clock)
    {
        LastCheckedAt = clock.UtcNow;
        Touch(clock);
    }

    public PublicationTarget AddTarget(
        string externalTargetId, TargetType type, TargetRole role, Guid? categoryId, string title,
        string? language, string? timeZone, int? characterLimit, IClock clock,
        bool showOnHome = false, string? publicUrl = null, int? followerCount = null)
    {
        var t = new PublicationTarget(Id, Platform, externalTargetId, type, role, categoryId, title, language, timeZone, characterLimit, clock, showOnHome, publicUrl, followerCount);
        _targets.Add(t);
        Touch(clock);
        return t;
    }
}
