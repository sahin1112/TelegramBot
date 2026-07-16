using ContentPlatform.Platform.Domain;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Application;

public interface ISocialAccountRepository
{
    Task<SocialAccount?> GetAsync(Guid id, CancellationToken ct);
    Task<PublicationTarget?> GetTargetAsync(Guid targetId, CancellationToken ct);
    Task AddAsync(SocialAccount account, CancellationToken ct);
    Task<IReadOnlyList<SocialAccount>> ListAsync(CancellationToken ct);
    Task<IReadOnlyList<SocialAccount>> ListByPlatformAsync(PlatformKind platform, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
