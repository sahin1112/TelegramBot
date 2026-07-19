using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Site.Api;
using ContentPlatform.Site.Application;
using ContentPlatform.Site.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentPlatform.Site;

/// <summary>Site bağlamının kompozisyon kökü — public blog (SSR) + SEO.</summary>
public sealed class SiteModule : IModule
{
    public string Name => "Site";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("Default")
                 ?? "Server=localhost,1433;Database=ContentPlatform;User Id=sa;Password=Sql159753!;TrustServerCertificate=True;";

        services.AddDbContext<SiteDbContext>(o => o.UseSqlServer(cs, sql =>
            sql.MigrationsHistoryTable("__ef_migrations", SiteDbContext.Schema)));
        services.AddScoped<IStartupMigrator, SiteMigrator>();

        services.Configure<SiteOptions>(configuration.GetSection("Site"));
        services.AddScoped<IBlogRepository, BlogRepository>();
        services.AddScoped<BlogQueryService>();
        services.AddScoped<CommentService>();
        services.AddScoped<NewsletterService>();
        services.AddScoped<IIntegrationEventHandler<ContentReadyToPublishIntegrationEvent>, ContentReadyToPublishBlogHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        BlogEndpoints.Map(endpoints);
        CommentEndpoints.Map(endpoints);
    }
}
