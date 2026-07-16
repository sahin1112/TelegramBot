using ContentPlatform.Abstractions;
using ContentPlatform.Publishing.Domain;

namespace ContentPlatform.Publishing.Application;

public interface IPublicationRepository
{
    Task<Publication?> FindAsync(Guid contentItemId, Channel channel, string targetRef, CancellationToken ct);
    Task<Publication?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Publication>> ListAsync(Guid? contentItemId, PublicationStatus? status, int take, CancellationToken ct);
    Task AddAsync(Publication publication, CancellationToken ct);
    Task<IReadOnlyList<Publication>> GetRetriableAsync(int maxAttempts, int take, CancellationToken ct);
    /// <summary>Zamanı gelmiş planlı yayınlar (Scheduled &amp; ScheduledAt ≤ now).</summary>
    Task<IReadOnlyList<Publication>> GetDueScheduledAsync(DateTimeOffset now, int take, CancellationToken ct);
    /// <summary>Planlı yayınlar (yayın zamanına göre artan) — panelde yönetim için.</summary>
    Task<IReadOnlyList<Publication>> GetScheduledAsync(int take, CancellationToken ct);
    /// <summary>Bir kategori için hâlâ planlı olan (Scheduled) yayınların farklı zamanları — slot çakışması hesabı.</summary>
    Task<IReadOnlyList<DateTimeOffset>> GetScheduledTimesForCategoryAsync(Guid? categoryId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
