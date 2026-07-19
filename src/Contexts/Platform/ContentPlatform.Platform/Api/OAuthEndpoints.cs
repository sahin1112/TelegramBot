using ContentPlatform.Platform.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace ContentPlatform.Platform.Api;

/// <summary>
/// Meta (Instagram/Threads) tek tık bağlantı uçları.
///  - /api/v1/platform/oauth/{platform}/start-url  → KORUMALI (panel çağırır); yetkilendirme URL'i döner.
///  - /oauth/{platform}/callback                   → HERKESE AÇIK (Meta çağırır); imzalı state ile korunur.
/// Redirect URI, Meta uygulamasında kayıtlı adresle BİREBİR aynı olmalı → Site:PublicBaseUrl kullanılır.
/// </summary>
internal static class OAuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/platform/oauth/{platform}/start-url",
            async (string platform, MetaOAuthService meta, GoogleOAuthService google, TikTokOAuthService tiktok,
                   XOAuthService x, IConfiguration cfg, HttpContext http, CancellationToken ct) =>
        {
            var b = PublicBase(cfg, http);
            var r = platform.ToLowerInvariant() switch
            {
                "youtube" => await google.BuildStartUrlAsync(b, ct),
                "tiktok" => await tiktok.BuildStartUrlAsync(b, ct),
                "x" => await x.BuildStartUrlAsync(b, ct),
                _ => await meta.BuildStartUrlAsync(platform, b, ct)
            };
            return r.IsSuccess ? Results.Ok(new { url = r.Value }) : Results.BadRequest(new { message = r.Error.Message });
        }).WithTags("Platform");

        app.MapGet("/oauth/{platform}/callback",
            async (string platform, string? code, string? state, string? error, string? error_description,
                   MetaOAuthService meta, GoogleOAuthService google, TikTokOAuthService tiktok,
                   XOAuthService x, IConfiguration cfg, HttpContext http, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(error))
                return Page(false, error_description ?? error!);
            if (string.IsNullOrWhiteSpace(code))
                return Page(false, "Platformdan yetki kodu gelmedi.");
            var b = PublicBase(cfg, http);
            var r = platform.ToLowerInvariant() switch
            {
                "youtube" => await google.HandleCallbackAsync(code!, state, b, ct),
                "tiktok" => await tiktok.HandleCallbackAsync(code!, state, b, ct),
                "x" => await x.HandleCallbackAsync(code!, state, b, ct),
                _ => await meta.HandleCallbackAsync(platform, code!, state, b, ct)
            };
            return Page(r.IsSuccess, r.IsSuccess ? r.Value : r.Error.Message);
        }).ExcludeFromDescription();
    }

    private static string PublicBase(IConfiguration cfg, HttpContext http)
    {
        var b = cfg["Site:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(b)) b = $"{http.Request.Scheme}://{http.Request.Host}";
        return b.TrimEnd('/');
    }

    /// <summary>Bağlantı sonucu penceresi: başarıda kendini kapatmayı dener; panele dönüş linki verir.
    /// NOT: HTML/CSS/JS süslü parantez dolu olduğu için interpolasyon DEĞİL placeholder+Replace kullanılır
    /// (raw string interpolasyonunda ardışık '}' karakterleri CS9007 derleme hatası veriyor).</summary>
    private static IResult Page(bool ok, string message)
    {
        var icon = ok ? "✅" : "⚠️";
        var title = ok ? "Hesap bağlandı" : "Bağlantı tamamlanamadı";
        var extra = ok
            ? $"<p><b>{Html(message)}</b> hesabı bağlandı ve yayın hedefi otomatik oluşturuldu.</p><p>Bu pencereyi kapatıp paneldeki <b>Sosyal Hesaplar</b> sayfasını yenileyin.</p>"
            : $"<p>{Html(message)}</p><p>Panele dönüp yeniden \"Bağlan\" deneyin.</p>";
        var script = ok
            ? "try{window.opener&&window.opener.postMessage('meta-connected','*');}catch(e){}setTimeout(function(){try{window.close();}catch(e){}},4000);"
            : "";
        var html = """
<!doctype html><html lang="tr"><head><meta charset="utf-8"><title>[[TITLE]]</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>body{font-family:system-ui,Segoe UI,Arial;background:#0b0b0f;color:#f4f7fc;display:grid;place-items:center;min-height:100vh;margin:0}
.card{max-width:440px;background:#14141a;border:1px solid #2a2a33;border-radius:14px;padding:28px 30px;text-align:center}
.ic{font-size:42px}h1{font-size:20px;margin:10px 0 4px}p{color:#c9d2e0;font-size:14px;line-height:1.55}
a{color:#f28d21}</style></head><body><div class="card"><div class="ic">[[ICON]]</div><h1>[[TITLE]]</h1>[[EXTRA]]
<p><a href="/HmbAdmin">Paneli aç</a></p></div>
<script>[[SCRIPT]]</script>
</body></html>
"""
            .Replace("[[TITLE]]", title)
            .Replace("[[ICON]]", icon)
            .Replace("[[EXTRA]]", extra)
            .Replace("[[SCRIPT]]", script);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
