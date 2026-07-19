using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using PlatformKind = ContentPlatform.Abstractions.Platform;
using TargetRole = ContentPlatform.Platform.Domain.TargetRole; // Abstractions.TargetRole ile çakışmayı çöz (hedef entity'nin rolü Domain enum'u)

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>
/// Bir içerik için hedefleri çözer: testMode ? Test : Editorial rolü; AdminInbox HER ZAMAN hariç.
/// CategoryId kapsamı: hedef ya bu kategoriye ait ya da tüm kategorilere açık (null).
/// </summary>
internal sealed class PublicationTargetResolver(PlatformDbContext db) : IPublicationTargetResolver
{
    public async Task<IReadOnlyList<ResolvedTarget>> ResolveAsync(
        Guid? categoryId, bool testMode, Channel channel, CancellationToken ct)
    {
        var platform = ToPlatform(channel);
        var role = testMode ? TargetRole.Test : TargetRole.Editorial; // AdminInbox asla seçilmez

        var targets = await db.PublicationTargets
            .Where(t => t.IsActive && t.Platform == platform && t.Role == role
                        && (t.CategoryId == null || t.CategoryId == categoryId))
            .ToListAsync(ct);

        return targets
            .Select(t => new ResolvedTarget(t.SocialAccountId, t.ExternalTargetId, channel))
            .ToList();
    }

    private static PlatformKind ToPlatform(Channel c) => c switch
    {
        Channel.Telegram => PlatformKind.Telegram,
        Channel.X => PlatformKind.X,
        Channel.Instagram => PlatformKind.Instagram,
        Channel.Threads => PlatformKind.Threads,
        Channel.Youtube => PlatformKind.Youtube,
        Channel.TikTok => PlatformKind.TikTok,
        _ => throw new NotSupportedException($"Kanal hedefe eşlenemez: {c}")
    };
}
