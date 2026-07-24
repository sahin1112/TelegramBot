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
        // OpenAI metin isteklerini süreç genelinde sıraya sokan tek kapı (SINGLETON) — atak/burst koruması.
        services.AddSingleton<Infrastructure.OpenAi.AiTextThrottle>();

        var cs = configuration.GetConnectionString("Default")
                 ?? "Server=localhost;Database=ContentPlatform;User Id=sa;Password=159753;TrustServerCertificate=True;";
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
        // OpenAI: uzun SEO içeriği üretimi 30 sn'yi aşabilir. Standart dayanıklılık işleyicisinin
        // varsayılan 30 sn TOPLAM zaman aşımı yetersiz; ayrıca pahalı/yinelenemez bir çağrıyı otomatik
        // yeniden denemek çift ücretlendirir. Bu yüzden burada sade, uzun (3 dk) zaman aşımı kullanıyoruz.
        services.AddHttpClient(OpenAiTextProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(3));
        // Görsel üretimi AYRI istemci: gpt-image-1 (özellikle 'high' kalitede) 3 dakikayı aşabiliyor —
        // metinle aynı istemciyi paylaşınca 180 sn'de "Timeout elapsed" kesiyordu.
        services.AddHttpClient(Infrastructure.OpenAi.OpenAiImageProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(8));

        // Meta (Instagram/Threads) gönderimi: video işleme beklemesi uzun sürebilir → sade, uzun zaman aşımı.
        services.AddHttpClient(Infrastructure.Meta.InstagramPublisher.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(2));
        // YouTube/TikTok video yükleme: dosya baytları gövdede taşınır → daha da uzun zaman aşımı.
        services.AddHttpClient(Infrastructure.Google.YoutubePublisher.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));

        // Kanal adaptörleri (yeni kanal = yeni IChannelPublisher; çekirdek değişmez).
        // Bir adaptör kaydedilince ContentReadyToPublishHandler o kanala OTOMATİK yayın açar.
        services.AddSingleton<IChannelPublisher, TelegramPublisher>();
        // Instagram: ISettingsProvider (scoped) kullandığı için SCOPED (registry de scoped — sorun yok).
        services.AddScoped<IChannelPublisher, Infrastructure.Meta.InstagramPublisher>();
        services.AddSingleton<IChannelPublisher, Infrastructure.Meta.ThreadsPublisher>();
        services.AddSingleton<IChannelPublisher, Infrastructure.Google.YoutubePublisher>();
        services.AddSingleton<IChannelPublisher, Infrastructure.TikTok.TikTokPublisher>();
        // X: ICredentialUpdater scoped (Platform) olduğundan XPublisher da SCOPED kaydedilir.
        services.AddScoped<IChannelPublisher, Infrastructure.X.XPublisher>();
        services.AddScoped<IChannelPublisherRegistry, ChannelPublisherRegistry>();

        // AI sağlayıcıları (soyutlama + ileride fallback).
        services.AddScoped<ITextGenerationProvider, OpenAiTextProvider>();
        services.AddScoped<IImageGenerationProvider, OpenAiImageProvider>();

        // Yayına-hazır içeriği hedeflere dağıtan handler.
        services.AddScoped<IIntegrationEventHandler<ContentReadyToPublishIntegrationEvent>, ContentReadyToPublishHandler>();
        // İçerik silinince bekleyen/planlı yayınları iptal et.
        services.AddScoped<IIntegrationEventHandler<ContentRetractedIntegrationEvent>, ContentRetractedPublishingHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PublishingEndpoints.Map(endpoints);
}
