using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using ContentPlatform.Worker;
using ContentPlatform.Worker.Jobs;
using Quartz;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Windows Servisi olarak calis (SCM ile konus + calisma dizinini exe klasorune sabitle).
builder.Services.AddWindowsService(o => o.ServiceName = "TelegramWorker");

// Konsol + metin + AYRI kritik hata gunlugu (worker-errors) + Api ile ORTAK JSON (.clef).
// Boylece /_diag/logs ucu worker loglarini da gosterir ([WRK] etiketiyle).
builder.Services.AddSerilog(cfg =>
{
    var logDir = builder.Configuration["Diagnostics:LogDirectory"];
    if (string.IsNullOrWhiteSpace(logDir))
        logDir = System.IO.Path.Combine(AppContext.BaseDirectory, "logs");
    System.IO.Directory.CreateDirectory(logDir);

    cfg.ReadFrom.Configuration(builder.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console()
       .WriteTo.File(System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "worker-.log"),
           rollingInterval: Serilog.RollingInterval.Day, retainedFileCountLimit: 10)
       .WriteTo.File(System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "worker-errors-.log"),
           restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error,
           rollingInterval: Serilog.RollingInterval.Day, retainedFileCountLimit: 30)
       .WriteTo.File(new ContentPlatform.Logging.JsonLogFormatter(),
           System.IO.Path.Combine(logDir, "worker-.clef"),
           rollingInterval: Serilog.RollingInterval.Day, retainedFileCountLimit: 10);
});
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

// -------- Telegram "/getid" komut dinleyicisi (long-polling) --------
// Botun bulundugu grup/kanalda "/getid" yazilinca chat_id'yi yanit gonderir.
// YALNIZ Worker'da; token sunucuda sifreli SocialAccount'tan cozulur.
builder.Services.AddHostedService<TelegramCommandPoller>();

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

    // Takılı (Pending) yayın kurtarma taraması: 30 sn'de bir (başarısızlar artık kendi gecikmeli planıyla döner)
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

// Bekleyen migration'ları uygula (Api ile aynı davranış; Worker sunucuda tek başına da ayağa kalkabilir).
if (builder.Configuration.GetValue("Database:AutoMigrate", true))
    await host.MigrateDatabaseAsync();

host.Run();
