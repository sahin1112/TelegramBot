using ContentPlatform.Abstractions;
using ContentPlatform.Ingestion.Api;
using ContentPlatform.Ingestion.Application;
using ContentPlatform.Ingestion.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentPlatform.Ingestion;

/// <summary>Ingestion bağlamı: kaynaklar + RSS okuma + dedup + FactPack + keşif.</summary>
public sealed class IngestionModule : IModule
{
    public string Name => "Ingestion";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("Default")
                 ?? "Server=localhost,1433;Database=ContentPlatform;User Id=sa;Password=Sql159753!;TrustServerCertificate=True;";

        services.AddDbContext<IngestionDbContext>(o => o.UseSqlServer(cs, sql =>
            sql.MigrationsHistoryTable("__ef_migrations", IngestionDbContext.Schema)));
        services.AddScoped<IStartupMigrator, IngestionMigrator>();

        services.AddHttpClient(RssFeedReader.HttpClientName);

        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<IDedupStore, DedupStore>();
        services.AddScoped<IFeedReader, RssFeedReader>();
        services.AddScoped<IFactPackExtractor, FactPackExtractor>();
        services.AddScoped<DiscoveryService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SourceEndpoints.Map(endpoints);
}
