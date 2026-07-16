using ContentPlatform.Platform.Domain;

namespace ContentPlatform.Platform.Application;

public interface ICategoryRepository
{
    Task<Category?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct);
    Task AddAsync(Category category, CancellationToken ct);
    void Remove(Category category);
    Task SaveChangesAsync(CancellationToken ct);
}
