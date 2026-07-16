using ContentPlatform.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Publishing.Infrastructure;

/// <summary>Publishing bağlamının bekleyen migration'larını uygular (startup'ta).</summary>
internal sealed class PublishingMigrator(PublishingDbContext db) : IStartupMigrator
{
    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}
