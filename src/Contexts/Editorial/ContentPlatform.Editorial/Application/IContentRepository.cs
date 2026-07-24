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
    /// <summary>Otomatik hazırlanacak TASLAK içeriklerin Id'leri (AutoContent/Image/Video açık ve ilgili adım henüz sonuçlanmamış).</summary>
    Task<IReadOnlyList<Guid>> GetAutoDraftCandidateIdsAsync(int take, CancellationToken ct);
    Task<IReadOnlyList<ContentItem>> GetAwaitingManualImageAsync(int take, CancellationToken ct);
    /// <summary>categoryId dolu → yalnız o kategori; uncategorized=true → yalnız kategorisiz; ikisi de boş → tümü.</summary>
    Task<(IReadOnlyList<ContentItem> Items, int Total)> GetPagedAsync(EditorialStatus? status, string? search, Guid? categoryId, bool uncategorized, int page, int size, bool ascending, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
