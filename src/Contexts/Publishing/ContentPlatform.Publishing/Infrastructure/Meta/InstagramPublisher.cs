using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Infrastructure.Meta;

/// <summary>
/// Instagram yayın adaptörü (Instagram API with Instagram Login — graph.instagram.com).
/// Kimlik: MetaOAuthService'in kaydettiği {AccessToken, UserId} (panelden "Hesap bağla").
/// Akış (Meta'nın 3 adımlı konteyner modeli):
///   1) POST /{user}/media → konteyner (video: media_type=REELS + video_url; foto: image_url)
///   2) video ise status_code=FINISHED olana dek bekle (3 sn arayla, en çok ~3 dk)
///   3) POST /{user}/media_publish → yayınla
/// NOTLAR: Medya PUBLIC URL'den verilir (Instagram kendisi indirir; multipart yok).
/// image_url YALNIZ JPEG kabul eder → PNG/WebP görseller sitedeki /media-jpg/{ad} dönüştürücüsüne çevrilir.
/// Metin-only paylaşım Instagram'da YOK — görsel/video yoksa açık hata döner.
/// </summary>
internal sealed class InstagramPublisher(
    IHttpClientFactory httpClientFactory,
    ISettingsProvider settings,
    ILogger<InstagramPublisher> logger) : IChannelPublisher
{
    public const string HttpClientName = "meta-publish";
    private const string Base = "https://graph.instagram.com/v23.0";
    public Channel Channel => Channel.Instagram;

    public async Task<PublishResult> PublishAsync(PublishRequest request, AccountCredentials credentials, CancellationToken ct)
    {
        if (!credentials.Values.TryGetValue("AccessToken", out var token) || string.IsNullOrWhiteSpace(token))
            return Fail("Instagram AccessToken yok — hesabı panelden 'Hesap bağla' ile (yeniden) bağlayın.");
        var userId = credentials.Values.TryGetValue("UserId", out var uid) && !string.IsNullOrWhiteSpace(uid)
            ? uid : request.TargetRef;

        var caption = BuildCaption(request);
        var hasVideo = IsPublicHttpUrl(request.VideoUrl);
        var imageUrl = IsPublicHttpUrl(request.MediaUrl) ? ToJpegUrl(request.MediaUrl!) : null;
        if (!hasVideo && imageUrl is null)
            return Fail("Instagram görselsiz/videosuz paylaşım kabul etmiyor — önce görsel ya da video üretin.");

        var client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            // ---- 1) Konteyner oluştur ----
            // VİDEO → REELS (share_to_feed: profil akışında da görünsün) · FOTO → media_type'sız = NORMAL AKIŞ GÖNDERİSİ.
            var form = new Dictionary<string, string> { ["access_token"] = token, ["caption"] = caption };
            if (hasVideo) { form["media_type"] = "REELS"; form["video_url"] = request.VideoUrl!; form["share_to_feed"] = "true"; }
            else form["image_url"] = imageUrl!;

            using var createResp = await SendWithNetRetryAsync(
                () => client.PostAsync($"{Base}/{userId}/media", new FormUrlEncodedContent(form), ct), ct);
            var createJson = await createResp.Content.ReadAsStringAsync(ct);
            if (!createResp.IsSuccessStatusCode)
                return Fail("Instagram konteyner hatası: " + ErrOf(createJson));
            var containerId = IdOf(createJson);
            if (containerId is null) return Fail("Instagram konteyner id alınamadı: " + Trim(createJson));

            // ---- 2) Video işlensin (Reels); foto genelde anında hazırdır ----
            if (hasVideo)
            {
                var ready = false;
                for (var i = 0; i < 60 && !ready; i++) // 60 × 3 sn ≈ 3 dk tavan
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    using var st = await client.GetAsync(
                        $"{Base}/{containerId}?fields=status_code&access_token={Uri.EscapeDataString(token)}", ct);
                    var stJson = await st.Content.ReadAsStringAsync(ct);
                    var code = FieldOf(stJson, "status_code");
                    if (code == "FINISHED") ready = true;
                    else if (code == "ERROR")
                        return Fail("Instagram videoyu işleyemedi: " + Trim(stJson));
                }
                if (!ready) return Fail("Instagram video işleme zaman aşımı (~3 dk) — daha sonra 'Yeniden dene'.");
            }

            // ---- 3) Yayınla ----
            using var pubResp = await SendWithNetRetryAsync(
                () => client.PostAsync($"{Base}/{userId}/media_publish",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["creation_id"] = containerId, ["access_token"] = token }), ct), ct);
            var pubJson = await pubResp.Content.ReadAsStringAsync(ct);
            if (!pubResp.IsSuccessStatusCode)
                return Fail("Instagram yayınlama hatası: " + ErrOf(pubJson));

            var mediaId = IdOf(pubJson);
            logger.LogInformation("Instagram'a yayınlandı: kullanıcı={User} media={Media} tür={Tur}",
                userId, mediaId, hasVideo ? "REELS" : "FOTO");

            // ---- 4) HİKAYE: aynı medya story olarak da paylaşılır — ARKA PLANDA (fire-and-forget) ----
            // Story'nin video işleme beklemesi asıl gönderimi DAKİKALARCA bloke ediyordu → panel isteği
            // zaman aşımına düşüyor, "Published" durumu KAYDEDİLEMİYOR ve yayın Scheduled kalıp planlı
            // saatte İKİNCİ KEZ gidebiliyordu. Artık asıl sonuç HEMEN döner; hikaye kendi başına,
            // iptal edilemez CancellationToken.None ile arka planda koşar ve sonucu yalnız loglanır.
            // NOT: API ile paylaşılan hikayelere TIKLANABİLİR link etiketi eklenemez (Instagram kısıtı);
            // kartta alan adı basılı, link profil bio'sunda.
            // Panel ayarı: instagram.story_enabled ("false" = hikaye HİÇ atılmaz). Varsayılan AÇIK.
            // Ayar her yayında okunur → panelden kapatmak deploy istemez, anında etkili.
            var storyOn = !string.Equals(await settings.GetAsync("instagram.story_enabled", ct), "false", StringComparison.OrdinalIgnoreCase);
            if (storyOn)
            {
                // Hikaye 9:16 olmalı: ayrı story görseli (StoryImageUrl) varsa onu (JPEG) kullan; yoksa 1:1'e düş.
                var storyImageUrl = IsPublicHttpUrl(request.StoryImageUrl) ? ToJpegUrl(request.StoryImageUrl!) : imageUrl;
                var storyVideoUrl = request.VideoUrl;
                _ = Task.Run(async () =>
                {
                    try { await PublishStoryAsync(client, userId, token, hasVideo, storyVideoUrl, storyImageUrl, CancellationToken.None); }
                    catch (Exception ex) { logger.LogWarning(ex, "Instagram hikaye (arka plan) başarısız — asıl gönderi yayında."); }
                });
            }

            return new PublishResult(true, mediaId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Instagram istek hatası");
            return Fail("Instagram istek hatası: " + ex.Message);
        }
    }

    /// <summary>Başlık + metin + hashtag. LINK KOYULMAZ — Instagram caption'da linkler tıklanamaz,
    /// yer kaplar ve spam görünür; ayrıntı yönlendirmesi "ayrıntılar profildeki linkte" satırıyla verilir.</summary>
    private static string BuildCaption(PublishRequest r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Title)) parts.Add(r.Title!);
        if (!string.IsNullOrWhiteSpace(r.Text) && r.Text != r.Title) parts.Add(r.Text);
        if (IsPublicHttpUrl(r.Link)) parts.Add("📌 Haberin ayrıntısı profildeki linkte.");
        if (r.Hashtags.Count > 0) parts.Add(string.Join(' ', r.Hashtags));
        var caption = string.Join("\n\n", parts);
        return caption.Length <= 2200 ? caption : caption[..2200];
    }

    /// <summary>Medyayı STORY olarak paylaşır: konteyner (media_type=STORIES) → (video ise işlenme
    /// beklenir) → media_publish. ÖNCE GÖRSEL tercih edilir: anında yayınlanır (video işleme
    /// beklemesi yok) ve kartta başlık + alan adı basılıdır; görsel yoksa videodan atılır.
    /// Story caption almaz; görünürlük 24 saattir.</summary>
    private async Task PublishStoryAsync(HttpClient client, string userId, string token,
        bool hasVideo, string? videoUrl, string? imageUrl, CancellationToken ct)
    {
        var useImage = imageUrl is not null; // görsel öncelikli
        var form = new Dictionary<string, string> { ["access_token"] = token, ["media_type"] = "STORIES" };
        if (useImage) { form["image_url"] = imageUrl!; hasVideo = false; }
        else if (hasVideo && IsPublicHttpUrl(videoUrl)) form["video_url"] = videoUrl!;
        else return; // paylaşılacak medya yok

        using var createResp = await client.PostAsync($"{Base}/{userId}/media", new FormUrlEncodedContent(form), ct);
        var createJson = await createResp.Content.ReadAsStringAsync(ct);
        if (!createResp.IsSuccessStatusCode)
        { logger.LogWarning("Instagram hikaye konteyner hatası: {Err}", ErrOf(createJson)); return; }
        var containerId = IdOf(createJson);
        if (containerId is null) { logger.LogWarning("Instagram hikaye konteyner id yok: {Json}", Trim(createJson)); return; }

        if (hasVideo)
        {
            var ready = false;
            for (var i = 0; i < 40 && !ready; i++) // ~2 dk tavan
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                using var st = await client.GetAsync(
                    $"{Base}/{containerId}?fields=status_code&access_token={Uri.EscapeDataString(token)}", ct);
                var code = FieldOf(await st.Content.ReadAsStringAsync(ct), "status_code");
                if (code == "FINISHED") ready = true;
                else if (code == "ERROR") { logger.LogWarning("Instagram hikaye videosu işlenemedi."); return; }
            }
            if (!ready) { logger.LogWarning("Instagram hikaye video işleme zaman aşımı."); return; }
        }

        using var pubResp = await client.PostAsync($"{Base}/{userId}/media_publish",
            new FormUrlEncodedContent(new Dictionary<string, string>
            { ["creation_id"] = containerId, ["access_token"] = token }), ct);
        var pubJson = await pubResp.Content.ReadAsStringAsync(ct);
        if (pubResp.IsSuccessStatusCode)
            logger.LogInformation("Instagram HİKAYE paylaşıldı: {Id}", IdOf(pubJson));
        else
            logger.LogWarning("Instagram hikaye yayınlama hatası: {Err}", ErrOf(pubJson));
    }

    /// <summary>IG image_url yalnız JPEG kabul eder: .jpg ise aynen; değilse sitedeki /media-jpg/{ad} dönüştürücüsü.</summary>
    internal static string ToJpegUrl(string url)
    {
        if (url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        var name = u.AbsolutePath.Split('/').LastOrDefault();
        return string.IsNullOrWhiteSpace(name) ? url : $"{u.Scheme}://{u.Authority}/media-jpg/{name}";
    }

    /// <summary>Anlık ağ/DNS kopmasında ("No such host" vb.) 2 sn bekleyip BİR kez daha dener —
    /// sunucu DNS'i ara ara çözemediğinde tek denemelik hatalar bununla kurtulur.</summary>
    private async Task<HttpResponseMessage> SendWithNetRetryAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try { return await send(); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Instagram ağ hatası — 2 sn sonra yeniden denenecek.");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return await send();
        }
    }

    private static PublishResult Fail(string msg) => new(false, null, Error.Unexpected(msg));

    private static string? IdOf(string json)
    { try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("id", out var v) ? v.GetString() : null; } catch { return null; } }

    private static string? FieldOf(string json, string field)
    { try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty(field, out var v) ? v.GetString() : null; } catch { return null; } }

    internal static string ErrOf(string json)
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
