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

app.UseSerilogRequestLogging();
app.UseResponseCompression();
app.UseDefaultFiles();
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
}); // wwwroot/admin (panel) 