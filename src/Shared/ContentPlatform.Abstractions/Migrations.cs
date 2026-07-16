using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ContentPlatform.Abstractions;

/// <summary>
/// Bir modülün DbContext'ini uygular. Her modül kendi migrator'ını kaydeder;
/// host uygulama ayağa kalkarken hepsini çalıştırır (sunucuda dışarıdan DB erişimi gerekmez).
/// </summary>
public interface IStartupMigrator
{
    Task MigrateAsync(CancellationToken ct);
}

public static class MigrationHostExtensions
{
    /// <summary>Kayıtlı tüm IStartupMigrator'ları çalıştırır (bekleyen migration'ları uygular).</summary>
    public static async Task MigrateDatabaseAsync(this IHost host, CancellationToken ct = default)
    {
        using var scope = host.Services.CreateScope();
        foreach (var migrator in scope.ServiceProvider.GetServices<IStartupMigrator>())
            await migrator.MigrateAsync(ct);
    }
}
