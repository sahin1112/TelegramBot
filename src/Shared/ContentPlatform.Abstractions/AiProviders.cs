namespace ContentPlatform.Abstractions;

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
