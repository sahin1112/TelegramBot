using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ContentPlatform.Site.Infrastructure;

/// <summary>
/// Design-time factory: 'dotnet ef migrations add' bunu kullanır (uygulamayı çalıştırmadan).
/// Bağlantı: CONTENTPLATFORM_DB ortam değişkeni ya da yereldeki SQL Server.
/// </summary>
internal sealed class SiteDbContextFactory : IDesignTimeDbContextFactory<SiteDbContext>
{
    public SiteDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CONTENTPLATFORM_DB")
                 ?? "Server=localhost;Database=ContentPlatform;User Id=sa;Password=159753;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<SiteDbContext>()
            .UseSqlServer(cs, sql => sql.MigrationsHistoryTable("__ef_migrations", SiteDbContext.Schema))
            .Options;
        return new SiteDbContext(options);
    }
}
