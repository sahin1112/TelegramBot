using System.Net.Http.Json;
using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Infrastructure.Telegram;

/// <summary>
/// Telegram yayın adaptörü — ham Bot API. Görsel varsa:
///  - bytes (Media) → multipart sendPhoto (public URL gerekmez)
///  - yalnız MediaUrl → URL ile sendPhoto
///  - görsel yok → sendMessage
/// Kimlik (BotToken) ÇAĞRIDA gelir; hedef = chat/kanal id (TargetRef).
///
/// DETAY LİNKİ: Kanal gönderisine inline BUTON eklenirse Telegram'ın otomatik "Yorum/Tartışma"
/// bölümü açılmıyor. Bu yüzden VARSAYILAN olarak detay linki METİN İÇİNE (caption) HTML bağlantı
/// olarak konur ve inline buton EKLENMEZ → yorumlar çalışır. appsettings "Telegram:DetailLinkInCaption=false"
/// ile eski buton davranışına dönülebilir.
/// </summary>
internal sealed class TelegramPublisher(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<TelegramPublisher> logger) : IChannelPublisher
{
    public const string HttpClientName = "telegram";
    public Channel Channel => Channel.Telegram;

    public async Task<PublishResult> PublishAsync(PublishRequest request, AccountCredentials credentials, CancellationToken ct)
    {
        if (!credentials.Values.TryGetValue("BotToken", out var token) || string.IsNullOrWhiteSpace(token))
            return new PublishResult(false, null, Error.Validation("Telegram BotToken eksik."));
        token = token.Trim();
        if (!TelegramToken.LooksValid(token))
        {
            // Yanlış girilmiş token (ör. token alanına şifre yazılması) → API'ye hiç gitme, net hata dön.
            logger.LogWarning("Telegram BotToken formatı geçersiz ('{Masked}') — hesap kimliğini panelden düzeltin.", TelegramToken.Mask(token));
            return new PublishResult(false, null, Error.Validation("Telegram BotToken formatı geçersiz (beklenen: 123456789:AA... — BotFather'dan alınır). Panel → Sosyal Hesaplar'dan düzeltin."));
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var baseUrl = $"https://api.telegram.org/bot{token}";

        // Detay linki metin içinde mi (buton yerine)? Varsayılan EVET → kanal yorumları açık kalır.
        // Yalnız açıkça "false" yazılırsa eski inline buton davranışına döner (IConfiguration indeksleyici;
        // Binder eklentisine bağımlılık yok).
        var linkInCaption = !string.Equals(configuration["Telegram:DetailLinkInCaption"], "false", StringComparison.OrdinalIgnoreCase);
        var caption = BuildText(request, linkInCaption);

        // Inline buton YALNIZ 'metin-içi link KAPALI' iken ve URL public ise eklenir. Caption modunda
        // reply_markup GÖNDERİLMEZ → kanalın otomatik yorum/tartışma bölümü çalışır. Public değilse
        // (localhost/göreli/boş) buton yine düşürülür — "inline keyboard button URL is invalid" hatasını önler.
        System.Text.Json.JsonElement? replyMarkup = (!linkInCaption && IsPublicHttpUrl(request.ButtonUrl))
            ? JsonSerializer.SerializeToElement(new { inline_keyboard = new[] { new[] { new { text = string.IsNullOrWhiteSpace(request.ButtonText) ? "Devamını oku" : request.ButtonText, url = request.ButtonUrl } } } })
            : null;

        try
        {
            HttpResponseMessage response;

            if (request.Media is { } media)
            {
                using var form = new MultipartFormDataContent
                {
                    { new StringContent(request.TargetRef), "chat_id" },
                    { new StringContent(caption), "caption" },
                    { new StringContent("HTML"), "parse_mode" }
                };
                if (replyMarkup is not null)
                    form.Add(new StringContent(replyMarkup.Value.GetRawText()), "reply_markup");
                var photo = new ByteArrayContent(media.Bytes);
                photo.Headers.TryAddWithoutValidation("Content-Type", media.ContentType);
                form.Add(photo, "photo", media.FileName);
                response = await client.PostAsync($"{baseUrl}/sendPhoto", form, ct);
            }
            else if (IsPublicHttpUrl(request.MediaUrl))
            {
                // Sadece Telegram'ın çekebileceği PUBLIC http(s) URL gönder. Göreli/localhost URL
                // ("/media/x.png") "invalid file HTTP URL: URL host is empty" verir → bu dala GİRME,
                // görselsiz metne düş (multipart bytes yolu zaten yukarıda; bu yalnız fallback).
                response = await client.PostAsJsonAsync($"{baseUrl}/sendPhoto",
                    JsonBody(replyMarkup, ("chat_id", request.TargetRef), ("photo", request.MediaUrl!), ("caption", caption), ("parse_mode", "HTML")), ct);
            }
            else if (request.VideoMedia is { } vm)
            {
                // VİDEO — dosya olarak (multipart sendVideo; en sağlam yol, public URL gerekmez).
                using var form = new MultipartFormDataContent
                {
                    { new StringContent(request.TargetRef), "chat_id" },
                    { new StringContent(caption), "caption" },
                    { new StringContent("HTML"), "parse_mode" },
                    { new StringContent("true"), "supports_streaming" }
                };
                if (replyMarkup is not null)
                    form.Add(new StringContent(replyMarkup.Value.GetRawText()), "reply_markup");
                var video = new ByteArrayContent(vm.Bytes);
                video.Headers.TryAddWithoutValidation("Content-Type", vm.ContentType);
                form.Add(video, "video", vm.FileName);
                response = await client.PostAsync($"{baseUrl}/sendVideo", form, ct);
            }
            else if (IsPublicHttpUrl(request.VideoUrl))
            {
                // VİDEO — yerel dosya okunamadıysa public URL yedeği (Telegram kendisi indirir; limit 20 MB).
                var body = JsonBody(replyMarkup, ("chat_id", request.TargetRef), ("video", request.VideoUrl!), ("caption", caption), ("parse_mode", "HTML"));
                body["supports_streaming"] = true;
                response = await client.PostAsJsonAsync($"{baseUrl}/sendVideo", body, ct);
            }
            else
            {
                response = await client.PostAsJsonAsync($"{baseUrl}/sendMessage",
                    JsonBody(replyMarkup, ("chat_id", request.TargetRef), ("text", caption), ("parse_mode", "HTML")), ct);
            }

            using (response)
            {
                var payload = await response.Content.ReadFromJsonAsync<TelegramResponse>(cancellationToken: ct);
                if (payload is { Ok: true })
                    return new PublishResult(true, payload.Result?.MessageId.ToString(), null);

                logger.LogWarning("Telegram yayın hatası: {Code} {Desc}", payload?.ErrorCode, payload?.Description);
                return new PublishResult(false, null, Error.Unexpected($"Telegram: {payload?.Description ?? response.StatusCode.ToString()}"));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram istek hatası");
            return new PublishResult(false, null, Error.Unexpected($"Telegram istek hatası: {ex.Message}"));
        }
    }

    /// <summary>
    /// JSON gövdesini sözlük olarak kurar; reply_markup YALNIZ doluysa eklenir.
    /// KRİTİK: "reply_markup": null GÖNDERİLMEZ — Telegram JSON gövdede null'ı obje sanmayıp
    /// "400 Bad Request: object expected as reply markup" döndürür (tüm kanal yayınlarını bozan hata buydu).
    /// </summary>
    private static Dictionary<string, object> JsonBody(JsonElement? replyMarkup, params (string Key, string Value)[] fields)
    {
        var body = new Dictionary<string, object>();
        foreach (var (k, v) in fields) body[k] = v;
        if (replyMarkup is { } rm) body["reply_markup"] = rm;
        return body;
    }

    private static string BuildText(PublishRequest r, bool linkInCaption)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Title)) parts.Add($"<b>{r.Title}</b>");
        if (!string.IsNullOrWhiteSpace(r.Text)) parts.Add(r.Text);
        if (r.Hashtags.Count > 0) parts.Add(string.Join(' ', r.Hashtags));

        if (linkInCaption)
        {
            // Detay linkini METİN İÇİNE HTML bağlantı olarak koy (buton yok → yorumlar açık).
            // Öncelik: mini-app derin linki (ButtonUrl); yoksa doğrudan makale linki (Link).
            var detailUrl = IsPublicHttpUrl(r.ButtonUrl) ? r.ButtonUrl : (IsPublicHttpUrl(r.Link) ? r.Link : null);
            if (detailUrl is not null)
            {
                var label = string.IsNullOrWhiteSpace(r.ButtonText) ? "Haber ayrıntısı için tıkla" : r.ButtonText;
                parts.Add($"🔗 <a href=\"{HtmlEscape(detailUrl)}\">{HtmlEscape(label)}</a>");
            }
        }
        else
        {
            // Eski davranış: buton public değilse (düşürülecek) ve Link public ise linki düz metne koy.
            if (IsPublicHttpUrl(r.Link) && !IsPublicHttpUrl(r.ButtonUrl)) parts.Add(r.Link!);
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>Telegram HTML parse_mode için '&lt; &gt; &amp;' kaçışı (link href ve etiket metni için).</summary>
    private static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Telegram'a verilebilecek geçerli, dışarıdan erişilebilir http(s) URL mi? localhost / 127.0.0.1 /
    /// göreli yollar (host'suz) REDDEDİLİR. Hem buton URL'si hem de foto URL fallback'i bununla korunur.
    /// </summary>
    private static bool IsPublicHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (host is "127.0.0.1" or "0.0.0.0" or "::1") return false;
        return true;
    }

    private sealed record TelegramResponse(
        bool Ok,
        TelegramMessage? Result,
        [property: System.Text.Json.Serialization.JsonPropertyName("error_code")] int? ErrorCode,
        string? Description);

    private sealed record TelegramMessage(
        [property: System.Text.Json.Serialization.JsonPropertyName("message_id")] long MessageId);
}
