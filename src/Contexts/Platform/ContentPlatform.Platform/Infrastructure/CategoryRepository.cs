using ContentPlatform.Platform.Application;
using ContentPlatform.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Platform.Infrastructure;

internal sealed class CategoryRepository(PlatformDbContext db) : ICategoryRepository
{
    public Task<Category?> GetAsync(Guid id, CancellationToken ct) =>
        db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct) =>
        await db.Categories.OrderBy(x => x.Name).ToListAsync(ct);
    public async Task AddAsync(Category category, CancellationToken ct) => await db.Categories.AddAsync(category, ct);
    public void Remove(Category category) => db.Categories.Remove(category);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
