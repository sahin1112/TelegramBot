using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Application;

/// <summary>
/// TikTok TEK TIK hesap bağlama (TikTok for Developers — Login Kit + Content Posting API).
/// Panelden tiktok.client_key / tiktok.client_secret BİR KEZ girilir; her hesap "Bağlan" ile eklenir.
/// Access token 24 SAATLİKTİR → hem günlük TokenRefreshJob yeniler hem de TikTokPublisher her
/// gönderimden önce taze token alır. Refresh token ~1 yıl geçerlidir.
/// </summary>
public sealed class TikTokOAuthService(
    ISocialAccountRepository accounts,
    ISettingsProvider settings,
    ICredentialProtector protector,
    IHttpClientFactory httpFactory,
    IClock clock,
    ILogger<TikTokOAuthService> logger)
{
    private const int StateTtlSeconds = 900;

    public async Task<Result<string>> BuildStartUrlAsync(string publicBaseUrl, CancellationToken ct)
    {
        var key = await settings.GetAsync("tiktok.client_key", ct);
        var secret = await settings.GetAsync("tiktok.client_secret", ct);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
            return Result.Failure<string>(Error.Conflict("Önce TikTok Client Key ve Client Secret'ı panelden kaydedin."));

        var state = protector.Protect($"tiktok|{clock.UtcNow.ToUnixTimeSeconds()}");
        var redirect = $"{publicBaseUrl}/oauth/tiktok/callback";
        return Result.Success(
            "https://www.tiktok.com/v2/auth/authorize/" +
            $"?client_key={Uri.EscapeDataString(key)}" +
            "&scope=" + Uri.EscapeDataString("user.info.basic,video.publish") +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
            $"&state={Uri.EscapeDataString(state)}");
    }

    public async Task<Result<string>> HandleCallbackAsync(string code, string? state, string publicBaseUrl, CancellationToken ct)
    {
        try
        {
            var raw = protector.Unprotect(state ?? "");
            var parts = raw.Split('|');
            if (parts.Length != 2 || parts[0] != "tiktok") return Result.Failure<string>(Error.Validation("Geçersiz state."));
            if (clock.UtcNow.ToUnixTimeSeconds() - long.Parse(parts[1]) > StateTtlSeconds)
                return Result.Failure<string>(Error.Validation("Bağlantı isteğinin süresi doldu — yeniden 'Bağlan' deyin."));
        }
        catch { return Result.Failure<string>(Error.Validation("Geçersiz state imzası — yeniden 'Bağlan' deyin.")); }

        var key = await settings.GetAsync("tiktok.client_key", ct);
        var secret = await settings.GetAsync("tiktok.client_secret", ct);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
            return Result.Failure<string>(Error.Conflict("TikTok Client Key/Secret ayarları bulunamadı."));

        var http = httpFactory.CreateClient(MetaOAuthService.HttpClientName);
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_key"] = key!,
                ["client_secret"] = secret!,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = $"{publicBaseUrl}/oauth/tiktok/callback"
            });
            using var resp = await http.PostAsync("https://open.tiktokapis.com/v2/oauth/token/", form, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            string? accessToken = null, refreshToken = null, openId = null;
            long expiresIn = 86400;
            using (var doc = JsonDocument.Parse(json))
            {
                var r = doc.RootElement;
                if (r.TryGetProperty("access_token", out var at)) accessToken = at.GetString();
                if (r.TryGetProperty("refresh_token", out var rt)) refreshToken = rt.GetString();
                if (r.TryGetProperty("open_id", out var oi)) openId = oi.GetString();
                if (r.TryGetProperty("expires_in", out var ei) && ei.TryGetInt64(out var v)) expiresIn = v;
            }
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(openId))
                return Result.Failure<string>(Error.Unexpected("TikTok token alınamadı: " + Trim(json)));

            // Görünen ad
            string? displayName = null;
            using (var req = new HttpRequestMessage(HttpMethod.Get,
                "https://open.tiktokapis.com/v2/user/info/?fields=open_id,display_name"))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                using var uResp = await http.SendAsync(req, ct);
                var uJson = await uResp.Content.ReadAsStringAsync(ct);
                try
                {
                    using var doc = JsonDocument.Parse(uJson);
                    if (doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("user", out var u) &&
                        u.TryGetProperty("display_name", out var dn)) displayName = dn.GetString();
                }
                catch { /* ad alınamazsa open_id kullanılır */ }
            }
            displayName ??= "TikTok " + openId[..Math.Min(8, openId.Length)];

            var creds = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["ClientKey"] = key!,
                ["ClientSecret"] = secret!,
                ["AccessToken"] = accessToken!,
                ["RefreshToken"] = refreshToken ?? "",
                ["OpenId"] = openId!
            });
            var encrypted = protector.Protect(creds);
            var expiresAt = clock.UtcNow.AddSeconds(expiresIn);

            var existing = (await accounts.ListByPlatformAsync(PlatformKind.TikTok, ct))
                .FirstOrDefault(a => SameKey(a, "OpenId", openId!));
            if (existing is not null)
            {
                existing.UpdateToken(encrypted, expiresAt, clock);
                await accounts.SaveChangesAsync(ct);
                return Result.Success(displayName + " (yeniden bağlandı)");
            }

            var account = new SocialAccount(PlatformKind.TikTok, displayName, encrypted, expiresAt, null, clock);
            account.AddTarget(openId!, Domain.TargetType.Profile, Domain.TargetRole.Editorial, null, displayName, "tr", null, null, clock);
            await accounts.AddAsync(account, ct);
            await accounts.SaveChangesAsync(ct);
            logger.LogInformation("TikTok hesabı bağlandı: {Name} ({OpenId})", displayName, openId);
            return Result.Success(displayName);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<string>(Error.Unexpected("TikTok'a ulaşılamadı (ağ): " + ex.Message));
        }
    }

    private bool SameKey(SocialAccount a, string key, string value)
    {
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Unprotect(a.CredentialsEncrypted));
            return map is not null && map.TryGetValue(key, out var v) && v == value;
        }
        catch { return false; }
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
