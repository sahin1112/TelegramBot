namespace ContentPlatform.Publishing.Infrastructure.OpenAi;

/// <summary>appsettings/ENV "OpenAI" bölümü. Anahtarlar ENV'den verilir.</summary>
public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string TextModel { get; set; } = "gpt-5.4-nano";
    public string ImageModel { get; set; } = "gpt-image-1-mini";
    // Fiyatlar (USD) — §27; ayarlardan da güncellenebilir.
    public decimal TextInputPer1M { get; set; } = 0.20m;
    public decimal TextOutputPer1M { get; set; } = 1.25m;
    public decimal ImagePerImage { get; set; } = 0.005m;
}
