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

    /// <summary>Ana sayfada gösterilecek hedefler: ShowOnHome + aktif + herkese açık URL'i olanlar.</summary>
    Task<IReadOnlyList<PublicationTarget>> ListHomeTargetsAsync(CancellationToken ct);

    /// <summary>Aynı hesapta aynı Dış ID'ye sahip (isteğe bağlı: verilen hedef hariç) başka hedef var mı?</summary>
    Task<bool> TargetExistsAsync(Guid socialAccountId, string externalTargetId, Guid? excludeTargetId, CancellationToken ct);

    /// <summary>Hedefi kalıcı olarak siler.</summary>
    void RemoveTarget(PublicationTarget target);

    /// <summary>Hesabı (ve hedeflerini, cascade) kalıcı olarak siler.</summary>
    void RemoveAccount(SocialAccount account);

    Task SaveChangesAsync(CancellationToken ct);
}
