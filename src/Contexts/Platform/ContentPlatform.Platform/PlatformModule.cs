using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Api;
using ContentPlatform.Platform.Application;
using ContentPlatform.Platform.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentPlatform.Platform;

/// <summary>Platform bağlamı: sosyal hesaplar + hedefler + kimlik şifreleme + token yenileme.</summary>
public sealed class PlatformModule : IModule
{
    public string Name => "Platform";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("Default")
                 ?? "Server=localhost,1433;Database=ContentPlatform;User Id=sa;Password=Sql159753!;TrustServerCertificate=True;";

        services.AddDbContext<PlatformDbContext>(o => o.UseSqlServer(cs, sql =>
            sql.MigrationsHistoryTable("__ef_migrations", PlatformDbContext.Schema)));
        services.AddScoped<IStartupMigrator, PlatformMigrator>();

        // Data Protection'ı BURADA yapılandırıyoruz çünkü PlatformModule hem Api hem Worker
        // tarafından kaydediliyor; ikisi de AYNI SABİT anahtar halkasını kullanmalı ki
        // Api'nin şifrelediği gizli değerleri (API anahtarı, sosyal token) Worker de çözebilsin.
        // Anahtarlar kalıcı klasöre yazılır; yoksa her yeniden başlatmada değişir ve çözme başarısız olur.
        var keysDir = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysDir))
            keysDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContentPlatform", "dp-keys");
        Directory.CreateDirectory(keysDir);
        services.AddDataProtection()
            .SetApplicationName("ContentPlatform")
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir));

        services.AddScoped<ICredentialProtector, DataProtectionCredentialProtector>();
        services.AddScoped<ISocialAccountRepository, SocialAccountRepository>();
        services.AddScoped<SocialAccountService>();

        // Token yenileme portu — Worker (TokenRefreshJob) bunu çözer.
        services.AddScoped<ITokenRefresher, TokenRefresher>();
        services.AddScoped<IPublicationTargetResolver, PublicationTargetResolver>();
        services.AddScoped<IAccountCredentialProvider, AccountCredentialProvider>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ISettingsProvider>(sp => sp.GetRequiredService<SettingsService>());
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<CategoryService>();

        // Kategori bazlı yayın kadansı politikası — Publishing (SchedulePlanner) bunu çözer.
        services.AddScoped<ISchedulePolicyProvider, CategorySchedulePolicyProvider>();

        // Acil durdurma: okuma portu (IKillSwitch, çekirdek akışlar) + yönetim (IKillSwitchAdmin, admin).
        services.AddScoped<KillSwitchService>();
        services.AddScoped<IKillSwitch>(sp => sp.GetRequiredService<KillSwitchService>());
        services.AddScoped<IKillSwitchAdmin>(sp => sp.GetRequiredService<KillSwitchService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PlatformEndpoints.Map(endpoints);
        CategoryEndpoints.Map(endpoints);
        KillSwitchEndpoints.Map(endpoints);
    }
}
