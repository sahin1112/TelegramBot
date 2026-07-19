using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Application;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>
/// Ana sayfa "Sosyalde ..." şeridini "Sosyal Hesaplar" bölümünden besler. Site bunu port üzerinden çözer.
/// Yalnız hedefte "ana sayfada yayınla" (ShowOnHome) seçili + aktif + herkese açık URL girilmiş kanallar döner.
/// Böylece bir platformda 5 kanal olsa bile ana sayfada YALNIZ seçilenler listelenir.
/// </summary>
internal sealed class PublicSocialProvider(ISocialAccountRepository accounts) : IPublicSocialProvider
{
    public async Task<IReadOnlyList<PublicSocialLink>> GetHomeLinksAsync(CancellationToken ct)
    {
        var targets = await accounts.ListHomeTargetsAsync(ct);
        return targets
            .Where(t => t.ShowOnHome && t.IsActive && !string.IsNullOrWhiteSpace(t.PublicUrl))
            .Select(t => new PublicSocialLink(t.Platform, t.Title, t.PublicUrl!, t.FollowerCount))
            .ToList();
    }
}
