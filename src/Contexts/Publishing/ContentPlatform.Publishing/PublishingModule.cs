using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Publishing.Api;
using ContentPlatform.Publishing.Application;
using ContentPlatform.Publishing.Infrastructure.OpenAi;
using ContentPlatform.Publishing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ContentPlatform.Publishing.Infrastructure.Telegram;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentPlatform.Publishing;

/// <summary>Publishing bağlamı: kanal adaptörleri + AI sağlayıcıları + dayanıklı HTTP.</summary>
public sealed class PublishingModule : IModule
{
    public string Name => "Publishing";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenAiOptions>(configuration.GetSection("OpenAI"));

        var cs = configuration.GetConnectionString("Default")
                 ?? "Server=localhost,1433;Database=ContentPlatform;User Id=sa;Password=Sql159753!;TrustServerCertificate=True;";
        services.AddDbContext<PublishingDbContext>(o => o.UseSqlServer(cs, sql =>
            sql.MigrationsHistoryTable("__ef_migrations", PublishingDbContext.Schema)));
        services.AddScoped<IStartupMigrator, PublishingMigrator>();
        services.AddScoped<IPublicationRepository, PublicationRepository>();
        services.AddScoped<ISchedulePlanner, SchedulePlanner>();
        services.AddScoped<DistributionService>();
        services.AddScoped<IUsageRecorder, UsageRecorder>();
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<UsageService>();

        // Dayanıklı adlandırılmış HttpClient'lar (retry + circuit breaker + timeout).
        services.AddHttpClient(TelegramPublisher.HttpClientName).AddStandardResilienceHandler();
        services.AddHttpClient(OpenAiTextProvider.HttpClientName).AddStandardResilienceHandler();

        // Kanal adaptörleri (yeni kanal = yeni IChannelPublisher; çekirdek değişmez).
        services.AddSingleton<IChannelPublisher, TelegramPublisher>();
        services.AddSingleton<IChannelPublisherRegistry, ChannelPublisherRegistry>();

        // AI sağlayıcıları (soyutlama + ileride fallback).
        services.AddScoped<ITextGenerationProvider, OpenAiTextProvider>();
        services.AddScoped<IImageGenerationProvider, OpenAiImageProvider>();

        // Yayına-hazır içeriği hedeflere dağıtan handler.
        services.AddScoped<IIntegrationEventHandler<ContentReadyToPublishIntegrationEvent>, ContentReadyToPublishHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PublishingEndpoints.Map(endpoints);
}
