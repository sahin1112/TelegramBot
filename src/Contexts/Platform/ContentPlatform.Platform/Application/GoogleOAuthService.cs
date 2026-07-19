using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Application;

/// <summary>
/// YouTube (Google) TEK TIK kanal bağlama. Panelden Google OAuth istemcisi (google.client_id /
/// google.client_secret) BİR KEZ girilir; sonra her YouTube kanalı "Bağlan" ile eklenir:
/// Google izin ekranı → kod → access + REFRESH token → kanal adı/ID çekilir → SocialAccount +
/// Editorial hedef otomatik kurulur. Refresh token kalıcıdır (Google Cloud'da uygulama
/// "In production" durumdayken); yayın anında YoutubePublisher bununla taze access token alır.
/// </summary>
public sealed class GoogleOAuthService(
    ISocialAccountRepository accounts,
    ISettingsProvider settings,
    ICredentialProtector protector,
    IHttpClientFactory httpFactory,
    IClock clock,
    ILogger<GoogleOAuthService> logger)
{
    private const int StateTtlSeconds = 900;

    public async Task<Result<string>> BuildStartUrlAsync(string publicBaseUrl, CancellationToken ct)
    {
        var clientId = await settings.GetAsync("google.client_id", ct);
        var secret = await settings.GetAsync("google.client_secret", ct);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return Result.Failure<string>(Error.Conflict("Önce Google Client ID ve Client Secret'ı panelden kaydedin."));

        var state = protector.Protect($"youtube|{clock.UtcNow.ToUnixTimeSeconds()}");
        var redirect = $"{publicBaseUrl}/oauth/youtube/callback";
        // youtube.upload = video yükleme; youtube.readonly = kanal adı/ID (görünen ad için).
        var scope = "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly";
        // access_type=offline + prompt=consent → REFRESH TOKEN garantilenir (tekrar bağlanmada da).
        return Result.Success(
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
            "&response_type=code&access_type=offline&prompt=consent" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&state={Uri.EscapeDataString(state)}");
    }

    public async Task<Result<string>> HandleCallbackAsync(string code, string? state, string publicBaseUrl, CancellationToken ct)
    {
        try
        {
            var raw = protector.Unprotect(state ?? "");
            var parts = raw.Split('|');
            if (parts.Length != 2 || parts[0] != "youtube") return Result.Failure<string>(Error.Validation("Geçersiz state."));
            if (clock.UtcNow.ToUnixTimeSeconds() - long.Parse(parts[1]) > StateTtlSeconds)
                return Result.Failure<string>(Error.Validation("Bağlantı isteğinin süresi doldu — yeniden 'Bağlan' deyin."));
        }
        catch { return Result.Failure<string>(Error.Validation("Geçersiz state imzası — yeniden 'Bağlan' deyin.")); }

        var clientId = await settings.GetAsync("google.client_id", ct);
        var secret = await settings.GetAsync("google.client_secret", ct);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return Result.Failure<string>(Error.Conflict("Google Client ID/Secret ayarları bulunamadı."));

        var http = httpFactory.CreateClient(MetaOAuthService.HttpClientName);
        try
        {
            // Kod → token (access + refresh)
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId!,
                ["client_secret"] = secret!,
                ["redirect_uri"] = $"{publicBaseUrl}/oauth/youtube/callback",
                ["grant_type"] = "authorization_code"
            });
            using var resp = await http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return Result.Failure<string>(Error.Unexpected($"Google token hatası ({(int)resp.StatusCode}): {Trim(json)}"));

            string? accessToken = null, refreshToken = null;
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("access_token", out var at)) accessToken = at.GetString();
                if (doc.RootElement.TryGetProperty("refresh_token", out var rt)) refreshToken = rt.GetString();
            }
            if (string.IsNullOrWhiteSpace(accessToken))
                return Result.Failure<string>(Error.Unexpected("Google access token alınamadı: " + Trim(json)));
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Result.Failure<string>(Error.Unexpected(
                    "Google REFRESH token vermedi. Google hesabınızın güvenlik sayfasından uygulamanın erişimini kaldırıp yeniden 'Bağlan' deyin."));

            // Kanal adı + ID
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var chResp = await http.SendAsync(req, ct);
            var chJson = await chResp.Content.ReadAsStringAsync(ct);
            string? channelId = null, channelTitle = null;
            if (chResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(chJson);
                if (doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    channelId = items[0].GetProperty("id").GetString();
                    if (items[0].TryGetProperty("snippet", out var sn) && sn.TryGetProperty("title", out var t))
                        channelTitle = t.GetString();
                }
            }
            if (channelId is null)
                return Result.Failure<string>(Error.Unexpected("YouTube kanalı bulunamadı — bu Google hesabında kanal var mı? " + Trim(chJson)));
            channelTitle ??= channelId;

            var creds = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["ClientId"] = clientId!,
                ["ClientSecret"] = secret!,
                ["RefreshToken"] = refreshToken!,
                ["ChannelId"] = channelId
            });
            var encrypted = protector.Protect(creds);

            var existing = (await accounts.ListByPlatformAsync(PlatformKind.Youtube, ct))
                .FirstOrDefault(a => SameKey(a, "ChannelId", channelId));
            if (existing is not null)
            {
                existing.UpdateToken(encrypted, clock.UtcNow.AddYears(10), clock); // refresh token kalıcı
                await accounts.SaveChangesAsync(ct);
                return Result.Success(channelTitle + " (yeniden bağlandı)");
            }

            var account = new SocialAccount(PlatformKind.Youtube, channelTitle, encrypted, null, null, clock);
            account.AddTarget(channelId, Domain.TargetType.Channel, Domain.TargetRole.Editorial, null, channelTitle, "tr", null, null, clock);
            await accounts.AddAsync(account, ct);
            await accounts.SaveChangesAsync(ct);
            logger.LogInformation("YouTube kanalı bağlandı: {Title} ({Id})", channelTitle, channelId);
            return Result.Success(channelTitle);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<string>(Error.Unexpected("Google'a ulaşılamadı (ağ): " + ex.Message));
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
