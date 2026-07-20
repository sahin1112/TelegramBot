using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContentPlatform.Abstractions;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Publishing.Infrastructure.OpenAi;

/// <summary>OpenAI görsel üretimi (düşük kalite). API anahtarı AYARLARDAN; maliyet kaydedilir.</summary>
internal sealed class OpenAiImageProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> options,
    ISettingsProvider settings,
    IUsageRecorder usage) : IImageGenerationProvider
{
    public const string HttpClientName = "openai-image"; // uzun zaman aşımı (PublishingModule: 8 dk)
    public string Name => "openai-image";
    private readonly OpenAiOptions _opt = options.Value;

    public async Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken ct)
    {
        var apiKey = await settings.GetAsync("OpenAI:ApiKey", ct) ?? _opt.ApiKey;
        var model = await settings.GetAsync("OpenAI:ImageModel", ct) ?? _opt.ImageModel;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "OpenAI API anahtarı yok ya da çözülemedi. Uygulama yeniden başladıysa Ayarlar'dan 'OpenAI:ApiKey' değerini tekrar kaydedin.");

        // Kalite ayardan gelebilir (OpenAI:ImageQuality: low/medium/high); yoksa istek varsayılanı.
        var quality = await settings.GetAsync("OpenAI:ImageQuality", ct) ?? request.Quality;
        // gpt-image-1 SABİT boyutlar kabul eder (1024x1024, 1024x1536, 1536x1024, auto).
        // Ayardan/istekten gelen serbest değer (ör. 1080x1080) orana göre en yakınına çevrilir —
        // yoksa API 400 'Invalid size' döndürüyor ve panel AI görsel üretemiyordu.
        var size = NormalizeSize(await settings.GetAsync("OpenAI:ImageSize", ct) ?? $"{request.Width}x{request.Height}");

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = new { model, prompt = request.Prompt, size, quality };

        // Anlık ağ/DNS kopmasında ("An error occurred while sending...") 2 sn bekleyip BİR kez daha dene.
        HttpResponseMessage resp;
        try { resp = await client.PostAsJsonAsync($"{_opt.BaseUrl}/images/generations", body, ct); }
        catch (HttpRequestException)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            resp = await client.PostAsJsonAsync($"{_opt.BaseUrl}/images/generations", body, ct);
        }
        using var _resp = resp;
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"OpenAI {(int)resp.StatusCode} ({resp.StatusCode}) — model '{model}': {(err.Length > 400 ? err[..400] : err)}");
        }
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var b64 = doc.RootElement.GetProperty("data")[0].GetProperty("b64_json").GetString() ?? "";
        var bytes = string.IsNullOrEmpty(b64) ? Array.Empty<byte>() : Convert.FromBase64String(b64);

        await usage.RecordAsync("openai", "image", 1, _opt.ImagePerImage, ct);
        return new ImageGenerationResult(bytes, "image/png", _opt.ImagePerImage);
    }

    /// <summary>Serbest "GxY" değerini gpt-image-1'in desteklediği boyuta indirger; çözülemezse "auto".</summary>
    internal static string NormalizeSize(string raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s is "1024x1024" or "1024x1536" or "1536x1024" or "auto") return s;
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+)\s*x\s*(\d+)$");
        if (!m.Success) return "auto";
        var w = int.Parse(m.Groups[1].Value);
        var h = int.Parse(m.Groups[2].Value);
        if (h <= 0) return "auto";
        var ratio = (double)w / h;
        if (ratio > 1.15) return "1536x1024";  // yatay
        if (ratio < 0.87) return "1024x1536";  // dikey
        return "1024x1024";                    // kare (1080x1080 buraya düşer)
    }
}
