using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Design-time factory: 'dotnet ef migrations add' bunu kullanır (uygulamayı çalıştırmadan).
/// Bağlantı: CONTENTPLATFORM_DB ortam değişkeni ya da yereldeki SQL Server.
/// </summary>
internal sealed class EditorialDbContextFactory : IDesignTimeDbContextFactory<EditorialDbContext>
{
    public EditorialDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CONTENTPLATFORM_DB")
                 ?? "Server=localhost;Database=ContentPlatform;User Id=sa;Password=159753;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<EditorialDbContext>()
            .UseSqlServer(cs, sql => sql.MigrationsHistoryTable("__ef_migrations", EditorialDbContext.Schema))
            .Options;
        return new EditorialDbContext(options);
    }
}
