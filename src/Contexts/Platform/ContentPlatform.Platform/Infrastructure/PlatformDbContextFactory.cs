using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>
/// Design-time factory: 'dotnet ef migrations add' bunu kullanır (uygulamayı çalıştırmadan).
/// Bağlantı: CONTENTPLATFORM_DB ortam değişkeni ya da yereldeki SQL Server.
/// </summary>
internal sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CONTENTPLATFORM_DB")
                 ?? "Server=localhost,1433;Database=ContentPlatform;User Id=sa;Password=Sql159753!;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(cs, sql => sql.MigrationsHistoryTable("__ef_migrations", PlatformDbContext.Schema))
            .Options;
        return new PlatformDbContext(options);
    }
}
