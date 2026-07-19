using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Infrastructure.Meta;

/// <summary>
/// Threads yayın adaptörü (graph.threads.net). Kimlik: {AccessToken, UserId} (panelden "Hesap bağla").
/// Akış: 1) POST /{user}/threads → konteyner (TEXT / IMAGE / VIDEO)
///       2) medyalıysa status=FINISHED bekle (video işleme sürebilir)
///       3) POST /{user}/threads_publish → yayınla.
/// Threads metin sınırı 500 karakter; medya PUBLIC URL'den verilir (JPEG+PNG kabul eder).
/// Görsel/video yoksa DÜZ METİN paylaşım yapılır (Threads destekler — Instagram'dan farkı).
/// </summary>
internal sealed class ThreadsPublisher(
    IHttpClientFactory httpClientFactory,
    ILogger<ThreadsPublisher> logger) : IChannelPublisher
{
    private const string Base = "https://graph.threads.net/v1.0";
    public Channel Channel => Channel.Threads;

    public async Task<PublishResult> PublishAsync(PublishRequest request, AccountCredentials credentials, CancellationToken ct)
    {
        if (!credentials.Values.TryGetValue("AccessToken", out var token) || string.IsNullOrWhiteSpace(token))
            return Fail("Threads AccessToken yok — hesabı panelden 'Hesap bağla' ile (yeniden) bağlayın.");
        var userId = credentials.Values.TryGetValue("UserId", out var uid) && !string.IsNullOrWhiteSpace(uid)
            ? uid : request.TargetRef;

        var text = BuildText(request);
        var hasVideo = IsPublicHttpUrl(request.VideoUrl);
        var hasImage = IsPublicHttpUrl(request.MediaUrl);

        var client = httpClientFactory.CreateClient(InstagramPublisher.HttpClientName);
        try
        {
            // ---- 1) Konteyner ----
            var form = new Dictionary<string, string> { ["access_token"] = token, ["text"] = text };
            if (hasVideo) { form["media_type"] = "VIDEO"; form["video_url"] = request.VideoUrl!; }
            else if (hasImage) { form["media_type"] = "IMAGE"; form["image_url"] = request.MediaUrl!; }
            else form["media_type"] = "TEXT";

            using var createResp = await client.PostAsync($"{Base}/{userId}/threads", new FormUrlEncodedContent(form), ct);
            var createJson = await createResp.Content.ReadAsStringAsync(ct);
            if (!createResp.IsSuccessStatusCode)
                return Fail("Threads konteyner hatası: " + InstagramPublisher.ErrOf(createJson));
            var containerId = IdOf(createJson);
            if (containerId is null) return Fail("Threads konteyner id alınamadı: " + Trim(createJson));

            // ---- 2) Medya işlensin (video uzun sürebilir; görsele de kısa bekleme) ----
            if (hasVideo || hasImage)
            {
                var tries = hasVideo ? 60 : 10; // video ~3 dk, görsel ~30 sn tavan
                var ready = false;
                for (var i = 0; i < tries && !ready; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    using var st = await client.GetAsync(
                        $"{Base}/{containerId}?fields=status,error_message&access_token={Uri.EscapeDataString(token)}", ct);
                    var stJson = await st.Content.ReadAsStringAsync(ct);
                    var code = FieldOf(stJson, "status");
                    if (code == "FINISHED") ready = true;
                    else if (code == "ERROR")
                        return Fail("Threads medyayı işleyemedi: " + Trim(stJson));
                }
                if (!ready) return Fail("Threads medya işleme zaman aşımı — daha sonra 'Yeniden dene'.");
            }

            // ---- 3) Yayınla ----
            using var pubResp = await client.PostAsync($"{Base}/{userId}/threads_publish",
                new FormUrlEncodedContent(new Dictionary<string, string>
                { ["creation_id"] = containerId, ["access_token"] = token }), ct);
            var pubJson = await pubResp.Content.ReadAsStringAsync(ct);
            if (!pubResp.IsSuccessStatusCode)
                return Fail("Threads yayınlama hatası: " + InstagramPublisher.ErrOf(pubJson));

            var mediaId = IdOf(pubJson);
            logger.LogInformation("Threads'e yayınlandı: kullanıcı={User} post={Post}", userId, mediaId);
            return new PublishResult(true, mediaId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Threads istek hatası");
            return Fail("Threads istek hatası: " + ex.Message);
        }
    }

    /// <summary>Metin + link + 1-2 hashtag, 500 karakter sınırıyla (öncelik: metin bütünlüğü, sonra link).</summary>
    private static string BuildText(PublishRequest r)
    {
        var text = !string.IsNullOrWhiteSpace(r.Text) ? r.Text : (r.Title ?? "");
        var link = IsPublicHttpUrl(r.Link) ? "\n\n🔗 " + r.Link : "";
        var tags = r.Hashtags.Count > 0 ? "\n" + string.Join(' ', r.Hashtags.Take(2)) : "";
        // 500 sınırı: önce hashtag'ler, sonra link feda edilir; metin en son kırpılır.
        var full = text + tags + link;
        if (full.Length <= 500) return full;
        full = text + link;
        if (full.Length <= 500) return full;
        return text.Length <= 500 ? text : text[..497] + "…";
    }

    private static PublishResult Fail(string msg) => new(false, null, Error.Unexpected(msg));

    private static string? IdOf(string json)
    { try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("id", out var v) ? v.GetString() : null; } catch { return null; } }

    private static string? FieldOf(string json, string field)
    { try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty(field, out var v) ? v.GetString() : null; } catch { return null; } }

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
