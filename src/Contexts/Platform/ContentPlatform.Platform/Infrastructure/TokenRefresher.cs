using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Application;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>
/// Süresi yaklaşan (7 günden az kalan) Instagram/Threads uzun ömürlü token'larını GERÇEK API ile yeniler:
///   IG:      GET graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token=...
///   Threads: GET graph.threads.net/refresh_access_token?grant_type=th_refresh_token&access_token=...
/// Yenilenen token yeniden şifrelenip hesaba yazılır (UpdateToken). Böylece 60 günlük token'lar
/// TokenRefreshJob (günlük) sayesinde süresiz yaşar. Hata olursa hesap Error işaretlenir, panelde görünür.
/// </summary>
internal sealed class TokenRefresher(
    ISocialAccountRepository repository,
    ICredentialProtector protector,
    IHttpClientFactory httpFactory,
    IClock clock,
    ILogger<TokenRefresher> logger) : ITokenRefresher
{
    private static readonly TimeSpan RefreshThreshold = TimeSpan.FromDays(7);

    public async Task<int> RefreshDueAsync(CancellationToken ct)
    {
        var accounts = await repository.ListAsync(ct);
        var due = accounts.Where(a => a.NeedsRefresh(clock, RefreshThreshold)).ToList();
        var refreshed = 0;
        if (due.Count == 0) return 0;

        var http = httpFactory.CreateClient(MetaOAuthService.HttpClientName);
        foreach (var account in due)
        {
            try
            {
                var json = protector.Unprotect(account.CredentialsEncrypted);
                var creds = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();

                HttpResponseMessage resp;
                if (account.Platform == PlatformKind.TikTok)
                {
                    // TikTok: refresh_token ile YENİ access token (24 saatlik) + dönebilecek yeni refresh token.
                    if (!creds.TryGetValue("RefreshToken", out var rt) || string.IsNullOrWhiteSpace(rt))
                    { account.MarkError("Token yenilenemedi: kayıtta RefreshToken yok — hesabı yeniden bağlayın.", clock); continue; }
                    creds.TryGetValue("ClientKey", out var ck); creds.TryGetValue("ClientSecret", out var cs);
                    using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_key"] = ck ?? "",
                        ["client_secret"] = cs ?? "",
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = rt
                    });
                    resp = await http.PostAsync("https://open.tiktokapis.com/v2/oauth/token/", form, ct);
                }
                else
                {
                    if (!creds.TryGetValue("AccessToken", out var token) || string.IsNullOrWhiteSpace(token))
                    { account.MarkError("Token yenilenemedi: kayıtta AccessToken yok.", clock); continue; }
                    var url = account.Platform == PlatformKind.Instagram
                        ? $"https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token={Uri.EscapeDataString(token)}"
                        : $"https://graph.threads.net/refresh_access_token?grant_type=th_refresh_token&access_token={Uri.EscapeDataString(token)}";
                    resp = await http.GetAsync(url, ct);
                }

                var body = await resp.Content.ReadAsStringAsync(ct);
                var ok = resp.IsSuccessStatusCode; var statusCode = (int)resp.StatusCode;
                resp.Dispose();
                if (!ok)
                {
                    account.MarkError($"Token yenileme hatası ({statusCode}): {(body.Length > 200 ? body[..200] : body)}", clock);
                    logger.LogWarning("Token yenileme reddi: {Platform} {Id} {Code}", account.Platform, account.Id, statusCode);
                    continue;
                }

                using var doc = JsonDocument.Parse(body);
                var newToken = doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
                long expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) && ei.TryGetInt64(out var v) ? v : 5184000;
                if (string.IsNullOrWhiteSpace(newToken))
                { account.MarkError("Token yenileme yanıtı çözülemedi.", clock); continue; }

                creds["AccessToken"] = newToken!;
                // TikTok yeni refresh token da dönebilir — dönerse sakla (eski geçersizleşebilir).
                if (doc.RootElement.TryGetProperty("refresh_token", out var nrt) && !string.IsNullOrWhiteSpace(nrt.GetString()))
                    creds["RefreshToken"] = nrt.GetString()!;
                account.UpdateToken(protector.Protect(JsonSerializer.Serialize(creds)), clock.UtcNow.AddSeconds(expiresIn), clock);
                logger.LogInformation("Token yenilendi: {Platform} {Name} (yeni bitiş: {Exp})", account.Platform, account.DisplayName, account.TokenExpiresAt);
                refreshed++;
            }
            catch (Exception ex)
            {
                account.MarkError($"Token yenilenemedi: {ex.Message}", clock);
                logger.LogWarning(ex, "Token yenileme hatası: {Id}", account.Id);
            }
        }

        await repository.SaveChangesAsync(ct);
        return refreshed;
    }
}
