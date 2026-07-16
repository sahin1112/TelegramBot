using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContentPlatform.Abstractions;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Publishing.Infrastructure.OpenAi;

/// <summary>OpenAI metin üretimi. API anahtarı/model AYARLARDAN (DB) okunur; maliyet kaydedilir.</summary>
internal sealed class OpenAiTextProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> options,
    ISettingsProvider settings,
    IUsageRecorder usage) : ITextGenerationProvider
{
    public const string HttpClientName = "openai";
    public string Name => "openai-text";
    private readonly OpenAiOptions _opt = options.Value;

    public async Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request, CancellationToken ct)
    {
        var apiKey = await settings.GetAsync("OpenAI:ApiKey", ct) ?? _opt.ApiKey;
        var model = await settings.GetAsync("OpenAI:TextModel", ct) ?? _opt.TextModel;

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

        using var resp = await client.PostAsJsonAsync($"{_opt.BaseUrl}/chat/completions", body, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        var usageEl = root.TryGetProperty("usage", out var u) ? u : default;
        var inTok = usageEl.ValueKind == JsonValueKind.Object ? usageEl.GetProperty("prompt_tokens").GetInt32() : 0;
        var outTok = usageEl.ValueKind == JsonValueKind.Object ? usageEl.GetProperty("completion_tokens").GetInt32() : 0;

        var cost = inTok / 1_000_000m * _opt.TextInputPer1M + outTok / 1_000_000m * _opt.TextOutputPer1M;
        await usage.RecordAsync("openai", "text", inTok + outTok, cost, ct);

        return new TextGenerationResult(content, model, inTok, outTok, cost);
    }
}
