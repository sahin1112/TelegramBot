using ContentPlatform.Editorial.Domain;

namespace ContentPlatform.Editorial.Application;

/// <summary>Application katmanı arayüzü tanımlar; Infrastructure uygular (bağımlılığın tersine çevrilmesi).</summary>
public interface IContentRepository
{
    Task<ContentItem?> GetAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsByHashAsync(string sourceHash, CancellationToken ct);
    Task AddAsync(ContentItem item, CancellationToken ct);
    Task<IReadOnlyList<ContentItem>> GetByStatusAsync(EditorialStatus status, int take, CancellationToken ct);
    Task<IReadOnlyList<ContentItem>> GetForGenerationAsync(int take, CancellationToken ct);
    Task<IReadOnlyList<ContentItem>> GetAwaitingManualImageAsync(int take, CancellationToken ct);
    Task<(IReadOnlyList<ContentItem> Items, int Total)> GetPagedAsync(EditorialStatus? status, string? search, int page, int size, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
