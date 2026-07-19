using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Infrastructure.Google;

/// <summary>
/// YouTube yayın adaptörü (YouTube Data API v3 — videos.insert, multipart yükleme).
/// Kimlik: {ClientId, ClientSecret, RefreshToken} (panelden "Bağlan" ya da elle giriş).
/// YALNIZ VİDEO: dikey ≤3 dk videolar YouTube'da otomatik SHORTS olur (başlığa #Shorts da eklenir).
/// Video baytları önce yerelden (VideoMedia) alınır; yoksa public VideoUrl'den indirilir.
/// ⚠️ Google Cloud projesi API DENETİMİNDEN geçmemişse YouTube yüklemeleri PRIVATE kilitlenebilir
/// (YouTube API Services compliance audit) — video yine yüklenir, görünürlüğü sonra elle açılabilir.
/// </summary>
internal sealed class YoutubePublisher(
    IHttpClientFactory httpClientFactory,
    ILogger<YoutubePublisher> logger) : IChannelPublisher
{
    public const string HttpClientName = "google-upload";
    public Channel Channel => Channel.Youtube;

    public async Task<PublishResult> PublishAsync(PublishRequest request, AccountCredentials credentials, CancellationToken ct)
    {
        if (!credentials.Values.TryGetValue("RefreshToken", out var refreshToken) || string.IsNullOrWhiteSpace(refreshToken))
            return Fail("YouTube RefreshToken yok — kanalı panelden 'Bağlan' ile bağlayın.");
        credentials.Values.TryGetValue("ClientId", out var clientId);
        credentials.Values.TryGetValue("ClientSecret", out var clientSecret);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return Fail("YouTube ClientId/ClientSecret eksik — kanalı yeniden bağlayın.");

        var client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            // ---- Video baytları: önce yerel dosya, yoksa public URL'den indir ----
            byte[]? video = request.VideoMedia?.Bytes;
            if (video is null && IsPublicHttpUrl(request.VideoUrl))
                video = await client.GetByteArrayAsync(request.VideoUrl!, ct);
            if (video is null)
                return Fail("YouTube yalnız video paylaşır — bu içerikte video yok (önce 🎬 Video oluştur).");

            // ---- Taze access token (refresh token ile) ----
            using var tokenForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId!,
                ["client_secret"] = clientSecret!,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            });
            using var tokenResp = await client.PostAsync("https://oauth2.googleapis.com/token", tokenForm, ct);
            var tokenJson = await tokenResp.Content.ReadAsStringAsync(ct);
            if (!tokenResp.IsSuccessStatusCode)
                return Fail("YouTube token yenileme hatası: " + Trim(tokenJson));
            string? accessToken;
            using (var doc = JsonDocument.Parse(tokenJson))
                accessToken = doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            if (string.IsNullOrWhiteSpace(accessToken))
                return Fail("YouTube access token alınamadı: " + Trim(tokenJson));

            // ---- Metadata + multipart yükleme ----
            var title = (string.IsNullOrWhiteSpace(request.Title) ? "Video" : request.Title!).Trim();
            if (!title.Contains("#Shorts", StringComparison.OrdinalIgnoreCase))
                title = title.Length > 90 ? title[..90] + " #Shorts" : title + " #Shorts";
            var descParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Text)) descParts.Add(request.Text);
            if (IsPublicHttpUrl(request.Link)) descParts.Add("🔗 " + request.Link);
            if (request.Hashtags.Count > 0) descParts.Add(string.Join(' ', request.Hashtags));
            var metadata = JsonSerializer.Serialize(new
            {
                snippet = new
                {
                    title = title.Length > 100 ? title[..100] : title,
                    description = string.Join("\n\n", descParts) is var d && d.Length > 4900 ? d[..4900] : d,
                    tags = request.Hashtags.Select(h => h.TrimStart('#')).Take(10)
                },
                status = new { privacyStatus = "public", selfDeclaredMadeForKids = false }
            });

            using var content = new MultipartContent("related");
            var meta = new StringContent(metadata, Encoding.UTF8, "application/json");
            content.Add(meta);
            var media = new ByteArrayContent(video);
            media.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            content.Add(media);

            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=multipart&part=snippet,status")
            { Content = content };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var resp = await client.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return Fail("YouTube yükleme hatası: " + ErrOf(json));

            string? videoId;
            using (var doc = JsonDocument.Parse(json))
                videoId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            logger.LogInformation("YouTube'a yüklendi: video={Id} başlık={Title}", videoId, title);
            return new PublishResult(true, videoId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "YouTube istek hatası");
            return Fail("YouTube istek hatası: " + ex.Message);
        }
    }

    private static PublishResult Fail(string msg) => new(false, null, Error.Unexpected(msg));

    private static string ErrOf(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
                return m.GetString() ?? Trim(json);
        }
        catch { }
        return Trim(json);
    }

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
