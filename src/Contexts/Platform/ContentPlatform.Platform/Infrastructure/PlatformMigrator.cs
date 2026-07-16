using ContentPlatform.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>Platform bağlamının bekleyen migration'larını uygular (startup'ta).</summary>
internal sealed class PlatformMigrator(PlatformDbContext db) : IStartupMigrator
{
    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}
