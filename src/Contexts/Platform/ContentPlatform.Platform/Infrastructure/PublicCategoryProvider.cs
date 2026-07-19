using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Application;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>Aktif kategorileri public siteye açar (üst menü + kategori sayfaları). Site bunu port üzerinden çözer.</summary>
internal sealed class PublicCategoryProvider(ICategoryRepository categories) : IPublicCategoryProvider
{
    public async Task<IReadOnlyList<PublicCategory>> GetActiveAsync(CancellationToken ct)
    {
        var all = await categories.ListAsync(ct);
        return all
            .Where(c => c.IsActive && !string.IsNullOrWhiteSpace(c.Slug))
            .OrderBy(c => c.Name, StringComparer.CurrentCulture)
            .Select(c => new PublicCategory(c.Id, c.Name, c.Slug))
            .ToList();
    }
}
