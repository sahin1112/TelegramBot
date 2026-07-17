using ContentPlatform.Abstractions;
using ContentPlatform.Ingestion.Contracts;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Editorial.Api;
using ContentPlatform.Editorial.Application;
using ContentPlatform.Editorial.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentPlatform.Editorial;

/// <summary>Editorial bağlamının kompozisyon kökü.</summary>
public sealed class EditorialModule : IModule
{
    public string Name => "Editorial";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("Default")
                 ?? "Server=localhost,1433;Database=ContentPlatform;User Id=sa;Password=Sql159753!;TrustServerCertificate=True;";

        services.AddDbContext<EditorialDbContext>(o => o.UseSqlServer(cs, sql =>
            sql.MigrationsHistoryTable("__ef_migrations", EditorialDbContext.Schema)));
        services.AddScoped<IStartupMigrator, EditorialMigrator>();

        services.Configure<MediaOptions>(configuration.GetSection("Media"));
        services.Configure<ContentPlatform.Abstractions.SiteOptions>(configuration.GetSection("Site"));
        services.AddSingleton<ICardRenderer, SkiaCardRenderer>();
        services.AddScoped<LocalMediaStore>();
        services.AddScoped<IMediaStore>(sp => sp.GetRequiredService<LocalMediaStore>());
        services.AddScoped<IMediaReader>(sp => sp.GetRequiredService<LocalMediaStore>());
        services.AddSingleton<IRiskClassifier, KeywordRiskClassifier>();
        services.AddSingleton<IQualityGate, ContentQualityGate>();
        services.AddScoped<IContentAudit, ContentAuditStore>();
        services.AddScoped<IContentRepository, ContentRepository>();
        services.AddScoped<IIntegrationEventHandler<ContentDiscoveredIntegrationEvent>, ContentDiscoveredHandler>();
        services.AddScoped<ContentGenerationService>();
        services.AddScoped<ManualContentService>();
        services.AddScoped<IIntegrationEventHandler<ContentPublishedIntegrationEvent>, ContentPublishedHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => EditorialEndpoints.Map(endpoints);
}
