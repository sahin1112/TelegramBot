using ContentPlatform.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Ingestion.Infrastructure;

/// <summary>Ingestion bağlamının bekleyen migration'larını uygular (startup'ta).</summary>
internal sealed class IngestionMigrator(IngestionDbContext db) : IStartupMigrator
{
    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}
