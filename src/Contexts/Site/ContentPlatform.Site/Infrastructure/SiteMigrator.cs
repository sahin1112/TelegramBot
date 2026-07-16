using ContentPlatform.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Site.Infrastructure;

/// <summary>Site bağlamının bekleyen migration'larını uygular (startup'ta).</summary>
internal sealed class SiteMigrator(SiteDbContext db) : IStartupMigrator
{
    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}
