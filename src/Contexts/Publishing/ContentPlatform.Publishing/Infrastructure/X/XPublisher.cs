using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Infrastructure.X;

/// <summary>
/// X (Twitter) yayın adaptörü — API v2 (OAuth2 kullanıcı bağlamı; panelden "Bağlan" ile kurulan hesap).
/// Akış: 1) access token'ı YENİLE (2 saatlik; refresh token TEK KULLANIMLIK → yenisi ICredentialUpdater
/// ile HEMEN kalıcı kayda yazılır — yazılmazsa bir sonraki gönderimde bağlantı kopar)
/// 2) medya yükle (v2 media/upload: görsel tek parça, video chunked INIT/APPEND/FINALIZE + STATUS)
/// 3) POST /2/tweets ile gönder. Metin 280 sınırına akıllı kırpılır (link t.co ile hep 23 sayılır).
/// Medya yüklenemezse gönderi DÜZ METİN olarak yine çıkar (X metin destekler — tüm yayın patlamaz).
/// </summary>
internal sealed class XPublisher(
    IHttpClientFactory httpClientFactory,
    ICredentialUpdater credentialUpdater,
    IAccountCredentialProvider credentialProvider,
    ILogger<XPublisher> logger) : IChannelPublisher
{
    private const string Api = "https://api.x.com/2";
    public Channel Channel => Channel.X;

    /// <summary>Hesap başına yenileme kilidi: aynı süreçte iki iş aynı anda refresh yapamaz.
    /// (X refresh token'ı TEK KULLANIMLIK — eşzamanlı iki yenileme birbirinin token'ını öldürür.)</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> RefreshLocks = new();

    public async Task<PublishResult> PublishAsync(PublishRequest request, AccountCredentials credentials, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(Google.YoutubePublisher.HttpClientName);
        try
        {
            // ---- 1) GEÇERLİ access token'ı al (ÇAKIŞMA KORUMALI) ----
            // Kural: token süresi dolmadıysa YENİLEME YAPILMAZ (rotasyon yok → çakışma imkânsız).
            // Yenileme gerekirse hesap bazında KİLİT altında, kayıt DB'den TAZE okunarak yapılır —
            // başka bir süreç/iş az önce yenilediyse onun token'ı kullanılır, ikinci yenileme atlanır.
            var (accessToken, tokenError) = await GetValidAccessTokenAsync(client, credentials, ct);
            if (accessToken is null)
                return Fail(tokenError ?? "X access token alınamadı.");

            // ---- 2) Medya yükle (opsiyonel; başarısızsa düz metinle devam) ----
            string? mediaId = null;
            try
            {
                if (request.Media is { } img)
                    mediaId = await UploadSimpleAsync(client, accessToken!, img.Bytes, img.ContentType, "tweet_image", ct);
                else if (request.VideoMedia is { } vid)
                    mediaId = await UploadChunkedAsync(client, accessToken!, vid.Bytes, "video/mp4", "tweet_video", ct);
                else if (IsPublicHttpUrl(request.MediaUrl))
                {
                    var bytes = await client.GetByteArrayAsync(request.MediaUrl!, ct);
                    mediaId = await UploadSimpleAsync(client, accessToken!, bytes, GuessType(request.MediaUrl!), "tweet_image", ct);
                }
                else if (IsPublicHttpUrl(request.VideoUrl))
                {
                    var bytes = await client.GetByteArrayAsync(request.VideoUrl!, ct);
                    mediaId = await UploadChunkedAsync(client, accessToken!, bytes, "video/mp4", "tweet_video", ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "X medya yüklenemedi — gönderi düz metin olarak atılacak.");
            }

            // ---- 3) Tweet ----
            var text = BuildText(request);
            object payload = mediaId is null
                ? new { text }
                : new { text, media = new { media_ids = new[] { mediaId } } };
            using var twReq = new HttpRequestMessage(HttpMethod.Post, $"{Api}/tweets")
            { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
            twReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var twResp = await client.SendAsync(twReq, ct);
            var twJson = await twResp.Content.ReadAsStringAsync(ct);
            if (!twResp.IsSuccessStatusCode)
                return Fail("X gönderi hatası: " + Trim(twJson));

            string? tweetId = null;
            using (var doc = JsonDocument.Parse(twJson))
                if (doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("id", out var idEl))
                    tweetId = idEl.GetString();
            logger.LogInformation("X'e gönderildi: tweet={Id} medya={Media}", tweetId, mediaId ?? "yok");
            return new PublishResult(true, tweetId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "X istek hatası");
            return Fail("X istek hatası: " + ex.Message);
        }
    }

    /// <summary>
    /// Geçerli access token döndürür. Süresi ≥5 dk kalmışsa eldeki kullanılır (yenileme YOK).
    /// Değilse: hesap kilidi al → kaydı DB'den TAZE oku (başka iş yenilemiş olabilir) → hâlâ eskiyse
    /// yenile; yenileme "invalid" derse BİR kez daha taze oku-dene (öteki sürecin kazandığı yarışta
    /// kendini iyileştirir). Yeni access+refresh token ANINDA kalıcılaştırılır (süresiyle birlikte).
    /// </summary>
    private async Task<(string? Token, string? Error)> GetValidAccessTokenAsync(HttpClient client, AccountCredentials credentials, CancellationToken ct)
    {
        static bool StillValid(IReadOnlyDictionary<string, string> v) =>
            v.TryGetValue("AccessToken", out var at) && !string.IsNullOrWhiteSpace(at) &&
            v.TryGetValue("AccessTokenExpiresAt", out var expS) &&
            DateTimeOffset.TryParse(expS, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var exp) &&
            exp > DateTimeOffset.UtcNow.AddMinutes(5);

        if (StillValid(credentials.Values))
            return (credentials.Values["AccessToken"], null);

        var gate = RefreshLocks.GetOrAdd(credentials.SocialAccountId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Kilit altında TAZE oku — başka iş az önce yenilediyse yenisini kullan, rotasyona girme.
            var fresh = await credentialProvider.GetAsync(credentials.SocialAccountId, ct) ?? credentials;
            if (StillValid(fresh.Values)) return (fresh.Values["AccessToken"], null);

            var vals = new Dictionary<string, string>(fresh.Values);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                vals.TryGetValue("RefreshToken", out var refreshToken);
                vals.TryGetValue("ClientId", out var clientId);
                vals.TryGetValue("ClientSecret", out var clientSecret);
                if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                    return (null, "X hesabı OAuth ile bağlanmamış — panelden '𝕏 Hesap bağla' ile bağlayın.");

                using var tReq = new HttpRequestMessage(HttpMethod.Post, $"{Api}/oauth2/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken! })
                };
                tReq.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")));
                using var tResp = await client.SendAsync(tReq, ct);
                var tJson = await tResp.Content.ReadAsStringAsync(ct);

                if (tResp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(tJson);
                    var r = doc.RootElement;
                    var newAccess = r.TryGetProperty("access_token", out var at) ? at.GetString() : null;
                    var newRefresh = r.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
                    long expiresIn = r.TryGetProperty("expires_in", out var ei) && ei.TryGetInt64(out var v) ? v : 7200;
                    if (string.IsNullOrWhiteSpace(newAccess))
                        return (null, "X token yanıtı çözülemedi: " + Trim(tJson));

                    vals["AccessToken"] = newAccess!;
                    if (!string.IsNullOrWhiteSpace(newRefresh)) vals["RefreshToken"] = newRefresh!;
                    vals["AccessTokenExpiresAt"] = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
                        .ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                    // KRİTİK: rotasyonlu refresh token HEMEN kalıcılaştırılır (iptal edilemez token'la).
                    await credentialUpdater.UpdateAsync(credentials.SocialAccountId, vals,
                        DateTimeOffset.UtcNow.AddSeconds(expiresIn), CancellationToken.None);
                    return (newAccess, null);
                }

                // Yenileme reddedildi. Yarış ihtimali: başka süreç token'ı bizden önce çevirip kaydetti
                // ve elimizdeki öldü → kaydı BİR kez daha taze okuyup (yeni refresh token'la) tekrar dene.
                logger.LogWarning("X token yenileme reddi (deneme {N}): {Body}", attempt + 1, Trim(tJson));
                if (attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    var reread = await credentialProvider.GetAsync(credentials.SocialAccountId, ct);
                    if (reread is not null)
                    {
                        if (StillValid(reread.Values)) return (reread.Values["AccessToken"], null);
                        vals = new Dictionary<string, string>(reread.Values);
                    }
                }
                else
                    return (null, "X token yenileme hatası — panelden '𝕏 Hesap bağla' ile hesabı yeniden bağlayın. Detay: " + Trim(tJson));
            }
            return (null, "X token alınamadı.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Görsel: tek istekte multipart yükleme (v2 media/upload).</summary>
    private static async Task<string?> UploadSimpleAsync(HttpClient client, string token, byte[] bytes, string contentType, string category, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(category), "media_category" }
        };
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(part, "media", "media" + (contentType.Contains("png") ? ".png" : ".jpg"));
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/media/upload") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("Görsel yükleme: " + Trim(json));
        return MediaIdOf(json);
    }

    /// <summary>Video: chunked yükleme — INIT → APPEND (4 MB parçalar) → FINALIZE → STATUS (işlenene dek).</summary>
    private static async Task<string?> UploadChunkedAsync(HttpClient client, string token, byte[] bytes, string contentType, string category, CancellationToken ct)
    {
        const string url = "https://api.x.com/2/media/upload";
        AuthenticationHeaderValue auth = new("Bearer", token);

        // INIT
        string? mediaId;
        using (var init = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["command"] = "INIT",
                ["total_bytes"] = bytes.Length.ToString(),
                ["media_type"] = contentType,
                ["media_category"] = category
            })
        })
        {
            init.Headers.Authorization = auth;
            using var resp = await client.SendAsync(init, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("Video INIT: " + Trim(json));
            mediaId = MediaIdOf(json);
            if (mediaId is null) throw new InvalidOperationException("Video INIT id yok: " + Trim(json));
        }

        // APPEND (4 MB parçalar)
        const int ChunkSize = 4 * 1024 * 1024;
        for (int offset = 0, seg = 0; offset < bytes.Length; offset += ChunkSize, seg++)
        {
            var len = Math.Min(ChunkSize, bytes.Length - offset);
            using var form = new MultipartFormDataContent
            {
                { new StringContent("APPEND"), "command" },
                { new StringContent(mediaId), "media_id" },
                { new StringContent(seg.ToString()), "segment_index" }
            };
            var part = new ByteArrayContent(bytes, offset, len);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, "media", "chunk");
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            req.Headers.Authorization = auth;
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Video APPEND {seg}: HTTP {(int)resp.StatusCode}");
        }

        // FINALIZE
        bool processing;
        using (var fin = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["command"] = "FINALIZE", ["media_id"] = mediaId })
        })
        {
            fin.Headers.Authorization = auth;
            using var resp = await client.SendAsync(fin, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("Video FINALIZE: " + Trim(json));
            processing = json.Contains("processing_info");
        }

        // STATUS — işlenme bitene dek (3 sn arayla, ~2 dk tavan)
        for (var i = 0; processing && i < 40; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            using var st = new HttpRequestMessage(HttpMethod.Get, $"{url}?command=STATUS&media_id={mediaId}");
            st.Headers.Authorization = auth;
            using var resp = await client.SendAsync(st, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (json.Contains("\"succeeded\"")) processing = false;
            else if (json.Contains("\"failed\"")) throw new InvalidOperationException("X videoyu işleyemedi: " + Trim(json));
        }
        if (processing) throw new InvalidOperationException("X video işleme zaman aşımı.");
        return mediaId;
    }

    /// <summary>media_id hem düz nesnede hem data{} içinde, string ya da sayı olarak gelebilir.</summary>
    private static string? MediaIdOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("data", out var d)) r = d;
            foreach (var key in new[] { "media_id_string", "id", "media_id" })
                if (r.TryGetProperty(key, out var v))
                    return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
        }
        catch { }
        return null;
    }

    /// <summary>280 sınırı: metin + link (t.co ile 23 sayılır) + sığarsa hashtag'ler.</summary>
    internal static string BuildText(PublishRequest r)
    {
        var text = !string.IsNullOrWhiteSpace(r.Text) ? r.Text : (r.Title ?? "");
        var link = IsPublicHttpUrl(r.Link) ? r.Link! : null;
        const int LinkCost = 25; // "\n\n" + t.co(23)
        var budget = 280 - (link is null ? 0 : LinkCost);

        if (text.Length > budget) text = text[..Math.Max(0, budget - 1)].TrimEnd() + "…";
        var tags = "";
        foreach (var tag in r.Hashtags.Take(3))
        {
            var cand = tags + " " + tag;
            if (text.Length + cand.Length <= budget) tags = cand; else break;
        }
        var result = text + (tags.Length > 0 ? "\n" + tags.Trim() : "");
        if (result.Length > budget) result = result[..budget];
        return link is null ? result : result + "\n\n" + link;
    }

    private static string GuessType(string url) =>
        url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" :
        url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : "image/jpeg";

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
