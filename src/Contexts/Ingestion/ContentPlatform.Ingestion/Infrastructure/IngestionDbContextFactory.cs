using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ContentPlatform.Ingestion.Infrastructure;

/// <summary>
/// Design-time factory: 'dotnet ef migrations add' bunu kullanır (uygulamayı çalıştırmadan).
/// Bağlantı: CONTENTPLATFORM_DB ortam değişkeni ya da yereldeki SQL Server.
/// </summary>
internal sealed class IngestionDbContextFactory : IDesignTimeDbContextFactory<IngestionDbContext>
{
    public IngestionDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CONTENTPLATFORM_DB")
                 ?? "Server=localhost,1433;Database=ContentPlatform;User Id=sa;Password=Sql159753!;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<IngestionDbContext>()
            .UseSqlServer(cs, sql => sql.MigrationsHistoryTable("__ef_migrations", IngestionDbContext.Schema))
            .Options;
        return new IngestionDbContext(options);
    }
}
