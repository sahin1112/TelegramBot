namespace ContentPlatform.Abstractions;

/// <summary>
/// OpenAI KALICI kota/faturalandırma hatası (429 insufficient_quota / billing_hard_limit). Geçici
/// rate-limit'ten FARKLIDIR: yeniden denemek düzeltmez — bakiye/limit sorunudur. Bu istisna alındığında
/// üretim otomatik hatta DENEME HAKKI YAKMADAN atlanır (kota açılınca kendiliğinden devam eder).
/// </summary>
public sealed class AiQuotaExceededException : Exception
{
    public AiQuotaExceededException(string message) : base(message) { }
}

public sealed record TextGenerationRequest(string SystemPrompt, string Input, string Language, string PromptVersion);
public sealed record TextGenerationResult(string RawJson, string Model, int InputTokens, int OutputTokens, decimal CostUsd);

public interface ITextGenerationProvider
{
    string Name { get; }
    Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request, CancellationToken ct);
}

public sealed record ImageGenerationRequest(string Prompt, int Width, int Height, string Quality);
public sealed record ImageGenerationResult(byte[] Bytes, string ContentType, decimal CostUsd);

public interface IImageGenerationProvider
{
    string Name { get; }
    Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken ct);
}
