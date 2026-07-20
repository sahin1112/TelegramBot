using System.Text.Json.Serialization;
using ContentPlatform.Abstractions;
using ContentPlatform.Api.Auth;
using ContentPlatform.Api.Diagnostics;
using ContentPlatform.SharedKernel;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// -------- Loglama (Serilog) --------
// Konsol + insan-okur metin + AYRI kritik hata gunlugu (api-errors) + Api/Worker ORTAK JSON (.clef).
// JSON dosyasini /_diag/logs ucu okuyup Api+Worker loglarini birlestirir.
builder.Host.UseSerilog((ctx, cfg) =>
{
    var logDir = ctx.Configuration["Diagnostics:LogDirectory"];
    if (string.IsNullOrWhiteSpace(logDir))
        logDir = System.IO.Path.Combine(AppContext.BaseDirectory, "logs");
    System.IO.Directory.CreateDirectory(logDir);

    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console()
       .WriteTo.File(System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "api-.log"),
           rollingInterval: Serilog.RollingInterval.Day, retainedFileCountLimit: 10)
       .WriteTo.File(System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "api-errors-.log"),
           restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error,
           rollingInterval: Serilog.RollingInterval.Day, retainedFileCountLimit: 30)
       .WriteTo.File(new ContentPlatform.Logging.JsonLogFormatter(),
           System.IO.Path.Combine(logDir, "api-.clef"),
           rollingInterval: Serilog.RollingInterval.Day, retainedFileCountLimit: 10);
});

// -------- Cross-cutting --------
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddIntegrationEventBus();

// Enum'lar JSON'da string olarak okunur/yazılır (panel "Telegram", "SkiaCard" vb. gönderir/bekler).
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// -------- Kimlik doğrulama (tek kullanıcı admin) --------
// NOT: Data Protection, PlatformModule içinde (Api+Worker ORTAK, sabit klasör) yapılandırılır.
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<AuthTokenService>();

// -------- Teşhis / uzaktan log akışı --------
builder.Services.Configure<DiagnosticsOptions>(builder.Configuration.GetSection("Diagnostics"));
builder.Services.AddSingleton<LogFeedReader>();

builder.Services.AddOpenApi();

// -------- Planlı yayın YEDEK göndericisi --------
// Asıl gönderici Worker'daki ScheduledDispatchJob; Worker durursa bile planlı yayınlar bu
// yedekle gider (atomik sahiplenme sayesinde ikisi aynı anda çalışsa da çifte gönderim olmaz).
builder.Services.AddHostedService<ContentPlatform.Api.ScheduledDispatchFallbackService>();

// -------- Performans: yanit sikistirma (Brotli/Gzip) --------
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

// -------- Modül keşfi (her bağlam bir IModule) --------
var moduleAssemblies = new[]
{
    typeof(ContentPlatform.Ingestion.IngestionModule).Assembly,
    typeof(ContentPlatform.Editorial.EditorialModule).Assembly,
    typeof(ContentPlatform.Publishing.PublishingModule).Assembly,
    typeof(ContentPlatform.Site.SiteModule).Assembly,
    typeof(ContentPlatform.Platform.PlatformModule).Assembly,
};
var modules = ModuleRegistrar.RegisterAll(builder.Services, builder.Configuration, moduleAssemblies);

var app = builder.Build();

// -------- Global hata yakalama --------
// İşlenmeyen her istisnayı kritik günlüğe yazar ve panele temiz bir JSON mesajı döndürür
// (ham 500 yerine). Pipeline'ın EN BAŞINDA olmalı ki tüm alt katmanları sarsın.
app.UseExceptionHandler(errApp => errApp.Run(async context =>
{
    var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var ex = feature?.Error;
    Log.Error(ex, "İşlenmeyen hata: {Method} {Path}", context.Request.Method, context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(new { message = "Sunucu hatası oluştu. Ayrıntı için sunucu günlüklerine (logs/api-errors) bakın." });
}));

app.UseSerilogRequestLogging();
app.UseResponseCompression();
app.UseDefaultFiles();
// /media, MediaOptions.StoragePath'ten servis edilir (Media:StoragePath MUTLAK ve Worker'la ORTAK
// olmalı — ör. C:\Datas\media). Böylece Telegram butonlarıyla WORKER'da üretilen görsel/video da
// panelde ve sitede görünür. Dosya burada yoksa bir sonraki (wwwroot) static middleware'e düşer —
// eski Api-wwwroot dosyaları çalışmaya devam eder.
{
    var mediaRoot = Path.GetFullPath(
        app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ContentPlatform.Editorial.Infrastructure.MediaOptions>>()
            .Value.StoragePath, AppContext.BaseDirectory);
    Directory.CreateDirectory(mediaRoot);
    app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
    {
        RequestPath = "/media",
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(mediaRoot),
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=2592000,immutable"
    });
}
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        if (path.StartsWith("/media", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "public,max-age=2592000,immutable"; // görseller: 30 gün
        else if (path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
}); // wwwroot/admin (panel) + wwwroot/media (görseller)
app.UseMiddleware<AuthMiddleware>(); // /api/v1/* korumalı (login hariç)
if (app.Environment.IsDevelopment()) app.MapOpenApi();

// Kök (/) artık Site modülünde ana sayfayı DOĞRUDAN render eder (redirect yok) → GTM/GA etiketi kökte de bulunur.
// Panel yalnız /HmbAdmin ile açılır.
app.MapGet("/HmbAdmin", () => Results.Redirect("/admin/index.html")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok", modules = modules.Select(m => m.Name) }))
   .WithTags("System");

// Modül endpoint'leri
AuthEndpoints.Map(app);
DiagnosticsEndpoints.Map(app); // /_diag/logs (gizli anahtar) + /api/v1/logs (admin)
ModuleRegistrar.MapAll(app, modules);

Log.Information("İçerik Platformu API başladı. Modüller: {Modules}", string.Join(", ", modules.Select(m => m.Name)));
// Bekleyen migration'ları uygula (sunucuda dışarıdan DB erişimi gerekmez).
if (app.Configuration.GetValue("Database:AutoMigrate", true))
    await app.MigrateDatabaseAsync();

app.Run();
