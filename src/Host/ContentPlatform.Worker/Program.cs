using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using ContentPlatform.Worker.Jobs;
using Quartz;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration).WriteTo.Console());
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddIntegrationEventBus();

// -------- Modül keşfi (Api ile aynı modüller; jobs bağlam servislerini DI'dan çözer) --------
ModuleRegistrar.RegisterAll(builder.Services, builder.Configuration, new[]
{
    typeof(ContentPlatform.Ingestion.IngestionModule).Assembly,
    typeof(ContentPlatform.Editorial.EditorialModule).Assembly,
    typeof(ContentPlatform.Publishing.PublishingModule).Assembly,
    typeof(ContentPlatform.Site.SiteModule).Assembly,
    typeof(ContentPlatform.Platform.PlatformModule).Assembly,
});

// -------- Quartz zamanlayıcı --------
builder.Services.AddQuartz(q =>
{
    // Kaynak tarama (iskelet)
    var pollKey = new JobKey(nameof(SourcePollJob));
    q.AddJob<SourcePollJob>(o => o.WithIdentity(pollKey));
    q.AddTrigger(t => t.ForJob(pollKey).WithIdentity($"{nameof(SourcePollJob)}-trigger")
        .WithSimpleSchedule(s => s.WithIntervalInMinutes(5).RepeatForever()));

    // İçerik üretimi (onaylananlar): her dakika
    var drainKey = new JobKey(nameof(PipelineDrainJob));
    q.AddJob<PipelineDrainJob>(o => o.WithIdentity(drainKey));
    q.AddTrigger(t => t.ForJob(drainKey).WithIdentity($"{nameof(PipelineDrainJob)}-trigger")
        .WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));

    // Başarısız yayınları yeniden dene: 30 sn'de bir
    var outboxKey = new JobKey(nameof(OutboxDispatchJob));
    q.AddJob<OutboxDispatchJob>(o => o.WithIdentity(outboxKey));
    q.AddTrigger(t => t.ForJob(outboxKey).WithIdentity($"{nameof(OutboxDispatchJob)}-trigger")
        .WithSimpleSchedule(s => s.WithIntervalInSeconds(30).RepeatForever()));

    // Zamanı gelen planlı yayınları gönder: dakikada bir
    var schedKey = new JobKey(nameof(ScheduledDispatchJob));
    q.AddJob<ScheduledDispatchJob>(o => o.WithIdentity(schedKey));
    q.AddTrigger(t => t.ForJob(schedKey).WithIdentity($"{nameof(ScheduledDispatchJob)}-trigger")
        .WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));

    // Token yenileme (günlük) — IG/Threads süreli token'ları
    var tokenKey = new JobKey(nameof(TokenRefreshJob));
    q.AddJob<TokenRefreshJob>(o => o.WithIdentity(tokenKey));
    q.AddTrigger(t => t.ForJob(tokenKey).WithIdentity($"{nameof(TokenRefreshJob)}-trigger")
        .WithSimpleSchedule(s => s.WithIntervalInHours(24).RepeatForever()));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

var host = builder.Build();
host.Run();
