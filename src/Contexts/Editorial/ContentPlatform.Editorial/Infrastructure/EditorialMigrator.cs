using ContentPlatform.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>Editorial bağlamının bekleyen migration'larını uygular (startup'ta).</summary>
internal sealed class EditorialMigrator(EditorialDbContext db) : IStartupMigrator
{
    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}
