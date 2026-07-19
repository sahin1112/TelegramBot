using ContentPlatform.Abstractions;
using ContentPlatform.Publishing.Domain;

namespace ContentPlatform.Publishing.Application;

public interface IPublicationRepository
{
    Task<Publication?> FindAsync(Guid contentItemId, Channel channel, string targetRef, CancellationToken ct);
    Task<Publication?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Publication>> ListAsync(Guid? contentItemId, PublicationStatus? status, int take, CancellationToken ct);
    /// <summary>Sayfalı + aramalı liste (hedef/hata metninde). Panel için.</summary>
    Task<(IReadOnlyList<Publication> Items, int Total)> ListPagedAsync(Guid? contentItemId, PublicationStatus? status, string? search, int page, int size, CancellationToken ct);
    Task AddAsync(Publication publication, CancellationToken ct);
    Task<IReadOnlyList<Publication>> GetRetriableAsync(int maxAttempts, int take, CancellationToken ct);
    /// <summary>Zamanı gelmiş planlı yayınlar (Scheduled &amp; ScheduledAt ≤ now).</summary>
    Task<IReadOnlyList<Publication>> GetDueScheduledAsync(DateTimeOffset now, int take, CancellationToken ct);
    /// <summary>
    /// Planlı yayını gönderim için ATOMİK sahiplen (DB'de Scheduled → Pending; tek sorguda, yalnız BİR süreç kazanır).
    /// Worker'daki ScheduledDispatchJob ile Api'deki yedek gönderici aynı anda çalışsa bile aynı yayın
    /// iki kez gönderilmez: true dönen gönderir, false dönen atlar.
    /// </summary>
    Task<bool> TryClaimScheduledAsync(Guid id, CancellationToken ct);
    /// <summary>Planlı yayınlar (yayın zamanına göre artan) — panelde yönetim için.</summary>
    Task<IReadOnlyList<Publication>> GetScheduledAsync(int take, CancellationToken ct);
    /// <summary>
    /// Bir kategorinin slot hesabına giren yayın zamanları: hâlâ planlı (Scheduled) olanlar
    /// + SON 48 SAATTE gönderilmiş/gönderilmekte olanlar (Published/Pending). Gönderilenler
    /// sayılmazsa kadans aralığı ve günlük tavan delinir (gönderim sonrası "hemen" planlanırdı).
    /// </summary>
    Task<IReadOnlyList<DateTimeOffset>> GetScheduledTimesForCategoryAsync(Guid? categoryId, DateTimeOffset now, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
