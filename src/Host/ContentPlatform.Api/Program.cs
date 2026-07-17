using System.Text.Json.Serialization;
using ContentPlatform.Abstractions;
using ContentPlatform.Api.Auth;
using ContentPlatform.SharedKernel;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// -------- Loglama (Serilog) --------
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

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
builder.Services.AddOpenApi();

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

app.UseSerilogRequestLogging();
app.UseDefaultFiles();
app.UseStaticFiles(); // wwwroot/admin (panel) + wwwroot/media (görseller)
app.UseMiddleware<AuthMiddleware>(); // /api/v1/* korumalı (login hariç)
if (app.Environment.IsDevelopment()) app.MapOpenApi();

// Health
app.MapGet("/", () => Results.Redirect("/admin/index.html")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok", modules = modules.Select(m => m.Name) }))
   .WithTags("System");

// Modül endpoint'leri
AuthEndpoints.Map(app);
ModuleRegistrar.MapAll(app, modules);

Log.Information("İçerik Platformu API başladı. Modüller: {Modules}", string.Join(", ", modules.Select(m => m.Name)));
// Bekleyen migration'ları uygula (sunucuda dışarıdan DB erişimi gerekmez).
if (app.Configuration.GetValue("Database:AutoMigrate", true))
    await app.MigrateDatabaseAsync();

app.Run();
