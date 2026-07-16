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
    public string Name => "openai-image";
    private readonly OpenAiOptions _opt = options.Value;

    public async Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken ct)
    {
        var apiKey = await settings.GetAsync("OpenAI:ApiKey", ct) ?? _opt.ApiKey;
        var model = await settings.GetAsync("OpenAI:ImageModel", ct) ?? _opt.ImageModel;

        var client = httpClientFactory.CreateClient(OpenAiTextProvider.HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = new { model, prompt = request.Prompt, size = $"{request.Width}x{request.Height}", quality = request.Quality };

        using var resp = await client.PostAsJsonAsync($"{_opt.BaseUrl}/images/generations", body, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var b64 = doc.RootElement.GetProperty("data")[0].GetProperty("b64_json").GetString() ?? "";
        var bytes = string.IsNullOrEmpty(b64) ? Array.Empty<byte>() : Convert.FromBase64String(b64);

        await usage.RecordAsync("openai", "image", 1, _opt.ImagePerImage, ct);
        return new ImageGenerationResult(bytes, "image/png", _opt.ImagePerImage);
    }
}
