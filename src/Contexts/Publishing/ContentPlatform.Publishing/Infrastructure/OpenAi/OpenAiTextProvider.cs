using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContentPlatform.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Publishing.Infrastructure.OpenAi;

/// <summary>OpenAI metin üretimi. API anahtarı/model AYARLARDAN (DB) okunur; maliyet kaydedilir.
/// İstekler AiTextThrottle ile SÜREÇ genelinde sıraya sokulur (atak/burst koruması); 429/5xx'te
/// bekleyip yeniden denenir; KALICI kota hatası (insufficient_quota) ise yeniden denenmeden durdurulur.</summary>
internal sealed class OpenAiTextProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> options,
    ISettingsProvider settings,
    IUsageRecorder usage,
    AiTextThrottle throttle,
    ILogger<OpenAiTextProvider> logger) : ITextGenerationProvider
{
    public const string HttpClientName = "openai";
    public string Name => "openai-text";
    private readonly OpenAiOptions _opt = options.Value;

    public async Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request, CancellationToken ct)
    {
        var apiKey = await settings.GetAsync("OpenAI:ApiKey", ct) ?? _opt.ApiKey;
        var model = await settings.GetAsync("OpenAI:TextModel", ct) ?? _opt.TextModel;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "OpenAI API anahtarı yok ya da çözülemedi. Uygulama yeniden başladıysa Ayarlar'dan 'OpenAI:ApiKey' değerini tekrar kaydedin.");

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.Input }
            },
            response_format = new { type = "json_object" }
        };

        // SÜREÇ genelinde SIRAYA sok (aynı anda sınırlı istek + aralarında boşluk) → "atak gibi" toplu
        // istekte OpenAI'nin reddetmesini önler. Kota tükendiyse RunAsync HTTP'ye çıkmadan hata verir.
        return await throttle.RunAsync(async token =>
        {
            var maxRetries = Math.Max(0, _opt.TextMaxRetries);
            for (var attempt = 0; ; attempt++)
            {
                using var resp = await client.PostAsJsonAsync($"{_opt.BaseUrl}/chat/completions", body, token);
                if (resp.IsSuccessStatusCode)
                {
                    using var stream = await resp.Content.ReadAsStreamAsync(token);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                    var root = doc.RootElement;
                    var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
                    var usageEl = root.TryGetProperty("usage", out var u) ? u : default;
                    var inTok = usageEl.ValueKind == JsonValueKind.Object ? usageEl.GetProperty("prompt_tokens").GetInt32() : 0;
                    var outTok = usageEl.ValueKind == JsonValueKind.Object ? usageEl.GetProperty("completion_tokens").GetInt32() : 0;

                    var cost = inTok / 1_000_000m * _opt.TextInputPer1M + outTok / 1_000_000m * _opt.TextOutputPer1M;
                    await usage.RecordAsync("openai", "text", inTok + outTok, cost, token);
                    return new TextGenerationResult(content, model, inTok, outTok, cost);
                }

                var status = (int)resp.StatusCode;
                var err = await resp.Content.ReadAsStringAsync(token);

                // KALICI kota/faturalandırma hatası: yeniden denemek ANLAMSIZ (bakiye/limit). Kısa süre AI'ı
                // durdur (aynı hatayı sıradaki tüm haberler için tekrar almamak için) ve net, ayrı bir hata fırlat.
                if (status == 429 && IsQuotaError(err))
                {
                    throttle.TripQuota();
                    logger.LogError("OpenAI KOTA/bakiye tükendi (insufficient_quota) — metin üretimi {Sn} sn duraklatıldı. model={Model}",
                        Math.Max(1, _opt.TextQuotaCooldownSeconds), model);
                    throw new AiQuotaExceededException(
                        "OpenAI kotanız/bakiyeniz tükenmiş görünüyor (insufficient_quota). platform.openai.com → Billing'i kontrol edin. " +
                        "Metin üretimi kısa süre otomatik duraklatıldı; düzeltince kendiliğinden devam eder. Ayrıntı: " +
                        (err.Length > 300 ? err[..300] : err));
                }

                // Geçici hata (gerçek rate-limit / anlık sunucu hatası): Retry-After ya da üstel bekleme ile yeniden dene.
                var transient = status is 429 or 500 or 502 or 503 or 504;
                if (transient && attempt < maxRetries)
                {
                    var delay = RetryAfter(resp) ?? TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt) * 2));
                    if (delay > TimeSpan.FromSeconds(60)) delay = TimeSpan.FromSeconds(60);
                    logger.LogWarning("OpenAI {Status} — {Delay} sn sonra yeniden denenecek ({Attempt}/{Max}). model={Model}",
                        status, (int)delay.TotalSeconds, attempt + 1, maxRetries, model);
                    await Task.Delay(delay, token);
                    continue;
                }

                throw new HttpRequestException(
                    $"OpenAI {status} ({resp.StatusCode}) — model '{model}': {(err.Length > 400 ? err[..400] : err)}");
            }
        }, ct);
    }

    /// <summary>Hata gövdesi KALICI kota/faturalandırma sorununa mı işaret ediyor? (rate_limit_exceeded'dan FARKLI).
    /// OpenAI bunu tutarlı biçimde code/type = "insufficient_quota" ile döndürür; yine de biçim değişse de
    /// yakalamak için gövdede anahtar ifadeler ARANIR (büyük/küçük harf duyarsız).</summary>
    private static bool IsQuotaError(string body)
    {
        var b = (body ?? "").ToLowerInvariant();
        return b.Contains("insufficient_quota")
            || b.Contains("billing_hard_limit")
            || b.Contains("exceeded your current quota")
            || b.Contains("check your plan and billing");
    }

    /// <summary>429/503 yanıtındaki Retry-After başlığını okur (saniye ya da tarih); yoksa null.</summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra is null) return null;
        if (ra.Delta is { } d && d > TimeSpan.Zero) return d;
        if (ra.Date is { } date)
        {
            var diff = date - DateTimeOffset.UtcNow;
            if (diff > TimeSpan.Zero) return diff;
        }
        return null;
    }
}
