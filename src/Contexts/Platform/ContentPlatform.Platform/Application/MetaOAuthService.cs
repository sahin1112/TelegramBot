using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Application;

/// <summary>
/// Meta (Instagram / Threads) TEK TIK hesap bağlama.
/// Akış: panele App ID/Secret girilir (ayarlar: meta.instagram.app_id / app_secret, meta.threads.*) →
/// "Bağlan" düğmesi yetkilendirme URL'ini açar → kullanıcı Meta'da izin verir →
/// /oauth/{platform}/callback koda düşer → kod KISA token'a, o da UZUN ÖMÜRLÜ (60 gün) token'a çevrilir →
/// profil (id + kullanıcı adı) çekilir → SocialAccount + Editorial hedef OTOMATİK oluşturulur
/// (aynı hesap ikinci kez bağlanırsa token GÜNCELLENİR, kopya açılmaz).
/// State CSRF koruması: DataProtection imzalı zaman damgası (15 dk geçerli; sunucu belleğine bağımlı değil).
/// </summary>
public sealed class MetaOAuthService(
    ISocialAccountRepository accounts,
    ISettingsProvider settings,
    ICredentialProtector protector,
    IHttpClientFactory httpFactory,
    IClock clock,
    ILogger<MetaOAuthService> logger)
{
    public const string HttpClientName = "meta-oauth";
    private const int StateTtlSeconds = 900;

    /// <summary>Yetkilendirme URL'ini kurar (panel bu URL'i yeni pencerede açar).</summary>
    public async Task<Result<string>> BuildStartUrlAsync(string platform, string publicBaseUrl, CancellationToken ct)
    {
        var p = Norm(platform);
        if (p is null) return Result.Failure<string>(Error.Validation("Platform 'instagram' ya da 'threads' olmalı."));

        var appId = await settings.GetAsync($"meta.{p}.app_id", ct);
        var secret = await settings.GetAsync($"meta.{p}.app_secret", ct);
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(secret))
            return Result.Failure<string>(Error.Conflict(
                $"Önce {(p == "instagram" ? "Instagram" : "Threads")} App ID ve App Secret'ı panelden kaydedin."));

        var state = protector.Protect($"{p}|{clock.UtcNow.ToUnixTimeSeconds()}");
        var redirect = $"{publicBaseUrl}/oauth/{p}/callback";
        var scope = p == "instagram"
            ? "instagram_business_basic,instagram_business_content_publish"
            : "threads_basic,threads_content_publish";
        var authorize = p == "instagram"
            ? "https://www.instagram.com/oauth/authorize"
            : "https://threads.net/oauth/authorize";

        return Result.Success(
            $"{authorize}?client_id={Uri.EscapeDataString(appId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
            $"&response_type=code&scope={Uri.EscapeDataString(scope)}" +
            $"&state={Uri.EscapeDataString(state)}");
    }

    /// <summary>Callback: kod → kısa token → uzun ömürlü token → profil → hesap kur/güncelle. Başarıda @kullanıcıadı döner.</summary>
    public async Task<Result<string>> HandleCallbackAsync(string platform, string code, string? state, string publicBaseUrl, CancellationToken ct)
    {
        var p = Norm(platform);
        if (p is null) return Result.Failure<string>(Error.Validation("Bilinmeyen platform."));

        // --- State (CSRF) doğrulama: imza bizden mi, platform tutuyor mu, süresi geçmemiş mi? ---
        try
        {
            var raw = protector.Unprotect(state ?? "");
            var parts = raw.Split('|');
            if (parts.Length != 2 || parts[0] != p) return Result.Failure<string>(Error.Validation("Geçersiz state (platform uyuşmuyor)."));
            if (clock.UtcNow.ToUnixTimeSeconds() - long.Parse(parts[1]) > StateTtlSeconds)
                return Result.Failure<string>(Error.Validation("Bağlantı isteğinin süresi doldu — panelden yeniden 'Bağlan' deyin."));
        }
        catch
        {
            return Result.Failure<string>(Error.Validation("Geçersiz state imzası — panelden yeniden 'Bağlan' deyin."));
        }

        var appId = await settings.GetAsync($"meta.{p}.app_id", ct);
        var secret = await settings.GetAsync($"meta.{p}.app_secret", ct);
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(secret))
            return Result.Failure<string>(Error.Conflict("App ID/Secret ayarları bulunamadı."));

        var redirect = $"{publicBaseUrl}/oauth/{p}/callback"; // authorize'dakiyle BİREBİR aynı olmalı
        var http = httpFactory.CreateClient(HttpClientName);
        try
        {
            // --- 1) Kod → kısa ömürlü token ---
            var tokenUrl = p == "instagram"
                ? "https://api.instagram.com/oauth/access_token"
                : "https://graph.threads.net/oauth/access_token";
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = appId!,
                ["client_secret"] = secret!,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirect,
                ["code"] = code
            });
            using var shortResp = await http.PostAsync(tokenUrl, form, ct);
            var shortJson = await shortResp.Content.ReadAsStringAsync(ct);
            if (!shortResp.IsSuccessStatusCode)
                return Result.Failure<string>(Error.Unexpected($"Token alınamadı ({(int)shortResp.StatusCode}): {Trim(shortJson)}"));
            var (shortToken, userIdFromToken) = ParseShortToken(shortJson);
            if (shortToken is null)
                return Result.Failure<string>(Error.Unexpected("Token yanıtı çözülemedi: " + Trim(shortJson)));

            // --- 2) Kısa → uzun ömürlü token (60 gün; TokenRefreshJob süresi yaklaşınca yeniler) ---
            var exchangeUrl = p == "instagram"
                ? $"https://graph.instagram.com/access_token?grant_type=ig_exchange_token&client_secret={Uri.EscapeDataString(secret!)}&access_token={Uri.EscapeDataString(shortToken)}"
                : $"https://graph.threads.net/access_token?grant_type=th_exchange_token&client_secret={Uri.EscapeDataString(secret!)}&access_token={Uri.EscapeDataString(shortToken)}";
            using var longResp = await http.GetAsync(exchangeUrl, ct);
            var longJson = await longResp.Content.ReadAsStringAsync(ct);
            var accessToken = shortToken; // uzun token alınamazsa kısa token'la devam (1 saat) — hesap yine kurulur
            long expiresIn = 3600;
            if (longResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(longJson);
                if (doc.RootElement.TryGetProperty("access_token", out var at)) accessToken = at.GetString() ?? shortToken;
                if (doc.RootElement.TryGetProperty("expires_in", out var ei) && ei.TryGetInt64(out var v)) expiresIn = v;
            }
            else
                logger.LogWarning("Uzun ömürlü token alınamadı ({Code}): {Body} — kısa token'la devam.", (int)longResp.StatusCode, Trim(longJson));

            // --- 3) Profil (id + kullanıcı adı) ---
            var meUrl = p == "instagram"
                ? $"https://graph.instagram.com/v23.0/me?fields=user_id,username&access_token={Uri.EscapeDataString(accessToken)}"
                : $"https://graph.threads.net/v1.0/me?fields=id,username&access_token={Uri.EscapeDataString(accessToken)}";
            using var meResp = await http.GetAsync(meUrl, ct);
            var meJson = await meResp.Content.ReadAsStringAsync(ct);
            string? userId = userIdFromToken, username = null;
            if (meResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(meJson);
                var r = doc.RootElement;
                if (r.TryGetProperty("username", out var un)) username = un.GetString();
                if (r.TryGetProperty("user_id", out var uid)) userId = uid.ValueKind == JsonValueKind.String ? uid.GetString() : uid.GetRawText();
                else if (r.TryGetProperty("id", out var id2)) userId = id2.ValueKind == JsonValueKind.String ? id2.GetString() : id2.GetRawText();
            }
            if (string.IsNullOrWhiteSpace(userId))
                return Result.Failure<string>(Error.Unexpected("Kullanıcı kimliği alınamadı: " + Trim(meJson)));
            username ??= userId;

            // --- 4) Hesap + hedef kur (aynı kullanıcı zaten bağlıysa token GÜNCELLE) ---
            var kind = p == "instagram" ? PlatformKind.Instagram : PlatformKind.Threads;
            var creds = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["AccessToken"] = accessToken,
                ["UserId"] = userId!,
                ["Username"] = username
            });
            var encrypted = protector.Protect(creds);
            var expiresAt = clock.UtcNow.AddSeconds(expiresIn);

            var existing = (await accounts.ListByPlatformAsync(kind, ct))
                .FirstOrDefault(a => SameUser(a, userId!));
            if (existing is not null)
            {
                existing.UpdateToken(encrypted, expiresAt, clock);
                await accounts.SaveChangesAsync(ct);
                logger.LogInformation("Meta hesabı yeniden bağlandı: {Platform} @{User}", kind, username);
                return Result.Success("@" + username + " (token yenilendi)");
            }

            var account = new SocialAccount(kind, "@" + username, encrypted, expiresAt, null, clock);
            // Profil hedefi: yayınlar doğrudan bu hesabın akışına gider (kategori = tümü; panelden değiştirilebilir).
            // (TargetRole hem Abstractions'ta hem Domain'de var → Domain'inki AÇIKÇA belirtilir; CS0104 önlenir.)
            account.AddTarget(userId!, Domain.TargetType.Profile, Domain.TargetRole.Editorial, null, "@" + username, "tr", null, null, clock);
            await accounts.AddAsync(account, ct);
            await accounts.SaveChangesAsync(ct);
            logger.LogInformation("Meta hesabı bağlandı: {Platform} @{User}", kind, username);
            return Result.Success("@" + username);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Meta OAuth ağ hatası ({Platform})", p);
            return Result.Failure<string>(Error.Unexpected("Meta'ya ulaşılamadı (ağ): " + ex.Message));
        }
    }

    /// <summary>Kısa token yanıtı iki biçimde gelebilir: düz nesne YA DA data[0] içinde.</summary>
    private static (string? Token, string? UserId) ParseShortToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array && d.GetArrayLength() > 0)
                r = d[0];
            string? token = r.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            string? uid = null;
            if (r.TryGetProperty("user_id", out var u))
                uid = u.ValueKind == JsonValueKind.String ? u.GetString() : u.GetRawText();
            return (token, uid);
        }
        catch { return (null, null); }
    }

    private bool SameUser(SocialAccount a, string userId)
    {
        try
        {
            var json = protector.Unprotect(a.CredentialsEncrypted);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return map is not null && map.TryGetValue("UserId", out var v) && v == userId;
        }
        catch { return false; }
    }

    private static string? Norm(string platform) => platform.Trim().ToLowerInvariant() switch
    {
        "instagram" => "instagram",
        "threads" => "threads",
        _ => null
    };

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
