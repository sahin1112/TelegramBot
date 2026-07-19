using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Application;

/// <summary>
/// X (Twitter) TEK TIK hesap bağlama — OAuth 2.0 + PKCE (X'te PKCE zorunlu).
/// Panelden x.client_id / x.client_secret BİR KEZ girilir; her hesap "Bağlan" ile eklenir.
/// PKCE code_verifier, imzalı state İÇİNDE taşınır (sunucu belleğine bağımlılık yok).
/// Access token 2 SAATLİKTİR; refresh token TEK KULLANIMLIK (her yenilemede döner) →
/// XPublisher her gönderimde yeniler ve ICredentialUpdater ile kalıcı kayda geri yazar.
/// </summary>
public sealed class XOAuthService(
    ISocialAccountRepository accounts,
    ISettingsProvider settings,
    ICredentialProtector protector,
    IHttpClientFactory httpFactory,
    IClock clock,
    ILogger<XOAuthService> logger)
{
    private const int StateTtlSeconds = 900;
    private const string Scopes = "tweet.read tweet.write users.read media.write offline.access";

    public async Task<Result<string>> BuildStartUrlAsync(string publicBaseUrl, CancellationToken ct)
    {
        var clientId = await settings.GetAsync("x.client_id", ct);
        var secret = await settings.GetAsync("x.client_secret", ct);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return Result.Failure<string>(Error.Conflict("Önce X Client ID ve Client Secret'ı panelden kaydedin."));

        // PKCE: verifier üret, challenge = BASE64URL(SHA256(verifier)); verifier imzalı state içinde taşınır.
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = protector.Protect($"x|{clock.UtcNow.ToUnixTimeSeconds()}|{verifier}");
        var redirect = $"{publicBaseUrl}/oauth/x/callback";

        return Result.Success(
            "https://x.com/i/oauth2/authorize" +
            $"?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
            $"&scope={Uri.EscapeDataString(Scopes)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256");
    }

    public async Task<Result<string>> HandleCallbackAsync(string code, string? state, string publicBaseUrl, CancellationToken ct)
    {
        string verifier;
        try
        {
            var raw = protector.Unprotect(state ?? "");
            var parts = raw.Split('|');
            if (parts.Length != 3 || parts[0] != "x") return Result.Failure<string>(Error.Validation("Geçersiz state."));
            if (clock.UtcNow.ToUnixTimeSeconds() - long.Parse(parts[1]) > StateTtlSeconds)
                return Result.Failure<string>(Error.Validation("Bağlantı isteğinin süresi doldu — yeniden 'Bağlan' deyin."));
            verifier = parts[2];
        }
        catch { return Result.Failure<string>(Error.Validation("Geçersiz state imzası — yeniden 'Bağlan' deyin.")); }

        var clientId = await settings.GetAsync("x.client_id", ct);
        var secret = await settings.GetAsync("x.client_secret", ct);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return Result.Failure<string>(Error.Conflict("X Client ID/Secret ayarları bulunamadı."));

        var http = httpFactory.CreateClient(MetaOAuthService.HttpClientName);
        try
        {
            // Kod → token (confidential client: Basic auth + PKCE verifier)
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = $"{publicBaseUrl}/oauth/x/callback",
                    ["code_verifier"] = verifier
                })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{secret}")));
            using var resp = await http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return Result.Failure<string>(Error.Unexpected($"X token hatası ({(int)resp.StatusCode}): {Trim(json)}"));

            string? accessToken = null, refreshToken = null; long expiresIn = 7200;
            using (var doc = JsonDocument.Parse(json))
            {
                var r = doc.RootElement;
                if (r.TryGetProperty("access_token", out var at)) accessToken = at.GetString();
                if (r.TryGetProperty("refresh_token", out var rt)) refreshToken = rt.GetString();
                if (r.TryGetProperty("expires_in", out var ei) && ei.TryGetInt64(out var v)) expiresIn = v;
            }
            if (string.IsNullOrWhiteSpace(accessToken))
                return Result.Failure<string>(Error.Unexpected("X access token alınamadı: " + Trim(json)));
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Result.Failure<string>(Error.Unexpected("X refresh token vermedi — uygulama ayarlarında 'offline.access' kapsamı ekli mi?"));

            // Profil (kullanıcı adı + id)
            using var meReq = new HttpRequestMessage(HttpMethod.Get, "https://api.x.com/2/users/me");
            meReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var meResp = await http.SendAsync(meReq, ct);
            var meJson = await meResp.Content.ReadAsStringAsync(ct);
            string? userId = null, username = null;
            if (meResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(meJson);
                if (doc.RootElement.TryGetProperty("data", out var d))
                {
                    userId = d.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    username = d.TryGetProperty("username", out var un) ? un.GetString() : null;
                }
            }
            if (string.IsNullOrWhiteSpace(userId))
                return Result.Failure<string>(Error.Unexpected("X kullanıcı bilgisi alınamadı: " + Trim(meJson)));
            username ??= userId;

            var creds = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["ClientId"] = clientId!,
                ["ClientSecret"] = secret!,
                ["AccessToken"] = accessToken!,
                // XPublisher süre dolmadan yenileme YAPMAZ (rotasyon çakışmasını önler) — süreyi kaydet.
                ["AccessTokenExpiresAt"] = clock.UtcNow.AddSeconds(expiresIn).ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["RefreshToken"] = refreshToken!,
                ["UserId"] = userId!,
                ["Username"] = username
            });
            var encrypted = protector.Protect(creds);
            var expiresAt = clock.UtcNow.AddSeconds(expiresIn);

            var existing = (await accounts.ListByPlatformAsync(PlatformKind.X, ct))
                .FirstOrDefault(a => SameKey(a, "UserId", userId!));
            if (existing is not null)
            {
                existing.UpdateToken(encrypted, expiresAt, clock);
                await accounts.SaveChangesAsync(ct);
                return Result.Success("@" + username + " (yeniden bağlandı)");
            }

            var account = new SocialAccount(PlatformKind.X, "@" + username, encrypted, expiresAt, null, clock);
            account.AddTarget(userId!, Domain.TargetType.Profile, Domain.TargetRole.Editorial, null, "@" + username, "tr", null, null, clock);
            await accounts.AddAsync(account, ct);
            await accounts.SaveChangesAsync(ct);
            logger.LogInformation("X hesabı bağlandı: @{User} ({Id})", username, userId);
            return Result.Success("@" + username);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<string>(Error.Unexpected("X'e ulaşılamadı (ağ): " + ex.Message));
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

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
