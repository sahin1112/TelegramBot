using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Infrastructure.TikTok;

/// <summary>
/// TikTok yayın adaptörü (Content Posting API — Direct Post + FILE_UPLOAD).
/// Kimlik: {ClientKey, ClientSecret, AccessToken, RefreshToken, OpenId} (panelden "Bağlan").
/// YALNIZ VİDEO. Akış: taze access token → creator_info sorgusu (izinli gizlilik seçenekleri) →
/// video init (tek parça FILE_UPLOAD) → PUT ile bayt yükleme → durum TikTok tarafında işlenir.
/// ⚠️ Uygulama TikTok DENETİMİNDEN (audit) geçmemişse gönderiler SELF_ONLY (yalnız sen) kalır —
/// kod izin verilen en açık gizlilik seviyesini otomatik seçer; denetim onaylanınca herkese açık olur.
/// </summary>
internal sealed class TikTokPublisher(
    IHttpClientFactory httpClientFactory,
    ILogger<TikTokPublisher> logger) : IChannelPublisher
{
    private const string Base = "https://open.tiktokapis.com/v2";
    public Channel Channel => Channel.TikTok;

    public async Task<PublishResult> PublishAsync(PublishRequest request, AccountCredentials credentials, CancellationToken ct)
    {
        credentials.Values.TryGetValue("AccessToken", out var accessToken);
        credentials.Values.TryGetValue("RefreshToken", out var refreshToken);
        credentials.Values.TryGetValue("ClientKey", out var clientKey);
        credentials.Values.TryGetValue("ClientSecret", out var clientSecret);
        if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(refreshToken))
            return Fail("TikTok token yok — hesabı panelden 'Bağlan' ile bağlayın.");

        var client = httpClientFactory.CreateClient(Google.YoutubePublisher.HttpClientName);
        try
        {
            // ---- Video baytları ----
            byte[]? video = request.VideoMedia?.Bytes;
            if (video is null && IsPublicHttpUrl(request.VideoUrl))
                video = await client.GetByteArrayAsync(request.VideoUrl!, ct);
            if (video is null)
                return Fail("TikTok yalnız video paylaşır — bu içerikte video yok (önce 🎬 Video oluştur).");

            // ---- Taze access token (24 saatlik olduğundan her gönderimde yenilemek en sağlamı) ----
            if (!string.IsNullOrWhiteSpace(refreshToken) && !string.IsNullOrWhiteSpace(clientKey) && !string.IsNullOrWhiteSpace(clientSecret))
            {
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_key"] = clientKey!,
                    ["client_secret"] = clientSecret!,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken!
                });
                using var tResp = await client.PostAsync($"{Base}/oauth/token/", form, ct);
                var tJson = await tResp.Content.ReadAsStringAsync(ct);
                if (tResp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(tJson);
                    if (doc.RootElement.TryGetProperty("access_token", out var at) && !string.IsNullOrWhiteSpace(at.GetString()))
                        accessToken = at.GetString();
                }
                else logger.LogWarning("TikTok token yenileme başarısız, eldeki token denenecek: {Body}", Trim(tJson));
            }
            if (string.IsNullOrWhiteSpace(accessToken))
                return Fail("TikTok access token alınamadı — hesabı yeniden bağlayın.");

            // ---- Creator info: izin verilen gizlilik seviyeleri (denetimsiz uygulamada SELF_ONLY olabilir) ----
            var privacy = "SELF_ONLY";
            using (var ciReq = new HttpRequestMessage(HttpMethod.Post, $"{Base}/post/publish/creator_info/query/"))
            {
                ciReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                ciReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var ciResp = await client.SendAsync(ciReq, ct);
                var ciJson = await ciResp.Content.ReadAsStringAsync(ct);
                if (!ciResp.IsSuccessStatusCode)
                    return Fail("TikTok creator bilgisi alınamadı: " + Trim(ciJson));
                try
                {
                    using var doc = JsonDocument.Parse(ciJson);
                    if (doc.RootElement.TryGetProperty("data", out var d) &&
                        d.TryGetProperty("privacy_level_options", out var opts) && opts.GetArrayLength() > 0)
                    {
                        var list = opts.EnumerateArray().Select(o => o.GetString()).Where(s => s is not null).ToList();
                        privacy = list.Contains("PUBLIC_TO_EVERYONE") ? "PUBLIC_TO_EVERYONE"
                                : list.Contains("MUTUAL_FOLLOW_FRIENDS") ? "MUTUAL_FOLLOW_FRIENDS"
                                : list[0]!;
                    }
                }
                catch { /* varsayılan SELF_ONLY ile devam */ }
            }

            // ---- Init (tek parça FILE_UPLOAD) ----
            var caption = BuildCaption(request);
            var initBody = JsonSerializer.Serialize(new
            {
                post_info = new
                {
                    title = caption,
                    privacy_level = privacy,
                    disable_duet = false,
                    disable_comment = false,
                    disable_stitch = false
                },
                source_info = new
                {
                    source = "FILE_UPLOAD",
                    video_size = video.Length,
                    chunk_size = video.Length,
                    total_chunk_count = 1
                }
            });
            string? publishId = null, uploadUrl = null;
            using (var initReq = new HttpRequestMessage(HttpMethod.Post, $"{Base}/post/publish/video/init/"))
            {
                initReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                initReq.Content = new StringContent(initBody, Encoding.UTF8, "application/json");
                using var initResp = await client.SendAsync(initReq, ct);
                var initJson = await initResp.Content.ReadAsStringAsync(ct);
                if (!initResp.IsSuccessStatusCode)
                    return Fail("TikTok init hatası: " + Trim(initJson));
                using var doc = JsonDocument.Parse(initJson);
                if (doc.RootElement.TryGetProperty("data", out var d))
                {
                    if (d.TryGetProperty("publish_id", out var p)) publishId = p.GetString();
                    if (d.TryGetProperty("upload_url", out var u)) uploadUrl = u.GetString();
                }
                if (publishId is null || uploadUrl is null)
                    return Fail("TikTok init yanıtı çözülemedi: " + Trim(initJson));
            }

            // ---- Baytları yükle (tek parça PUT + Content-Range) ----
            using (var putReq = new HttpRequestMessage(HttpMethod.Put, uploadUrl))
            {
                var body = new ByteArrayContent(video);
                body.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
                body.Headers.ContentRange = new ContentRangeHeaderValue(0, video.Length - 1, video.Length);
                putReq.Content = body;
                using var putResp = await client.SendAsync(putReq, ct);
                if (!putResp.IsSuccessStatusCode)
                    return Fail($"TikTok video yükleme hatası (HTTP {(int)putResp.StatusCode}).");
            }

            logger.LogInformation("TikTok'a gönderildi: publish={Id} gizlilik={Privacy}", publishId, privacy);
            var note = privacy == "PUBLIC_TO_EVERYONE" ? null
                : $" (gizlilik: {privacy} — uygulama TikTok denetiminden geçince herkese açık olur)";
            return new PublishResult(true, publishId + note, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TikTok istek hatası");
            return Fail("TikTok istek hatası: " + ex.Message);
        }
    }

    /// <summary>Başlık/metin + 2-3 hashtag (TikTok başlığı ~2200 karaktere kadar; kısa tutmak daha iyi).</summary>
    private static string BuildCaption(PublishRequest r)
    {
        var text = !string.IsNullOrWhiteSpace(r.Title) ? r.Title! : r.Text;
        var tags = r.Hashtags.Count > 0 ? " " + string.Join(' ', r.Hashtags.Take(3)) : "";
        var full = text + tags;
        return full.Length <= 2000 ? full : full[..2000];
    }

    private static PublishResult Fail(string msg) => new(false, null, Error.Unexpected(msg));
    private static string Trim(string s) => s.Length > 250 ? s[..250] : s;

    private static bool IsPublicHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrWhiteSpace(uri.Host)) return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (uri.Host is "127.0.0.1" or "0.0.0.0" or "::1") return false;
        return true;
    }
}
