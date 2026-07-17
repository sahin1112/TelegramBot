using System.Net.Http;
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

        // WebPage okuyucu için SSRF sertleştirmeli HttpClient (yerel/özel/metadata IP engeli, redirect sınırı).
        services.AddHttpClient(WebPageFeedReader.HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(15);
                c.MaxResponseContentBufferSize = 4_000_000;
                c.DefaultRequestHeaders.UserAgent.ParseAdd("ContentPlatformBot/1.0 (+ingestion)");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ConnectCallback = SsrfGuard.ConnectAsync
            });

        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<IDedupStore, DedupStore>();
        services.AddScoped<IFeedReader, RssFeedReader>();
        services.AddScoped<IFeedReader, WebPageFeedReader>();
        services.AddScoped<IFactPackExtractor, FactPackExtractor>();
        services.AddScoped<DiscoveryService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SourceEndpoints.Map(endpoints);
}
