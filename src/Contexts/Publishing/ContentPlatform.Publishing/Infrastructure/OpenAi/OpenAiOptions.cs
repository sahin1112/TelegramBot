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

    // --- METİN isteği hız sınırı (atak/burst koruması) — bkz. AiTextThrottle ---
    /// <summary>Aynı anda EN ÇOK kaç OpenAI metin isteği (SÜREÇ geneli). Toplu üretimde 1 önerilir.</summary>
    public int TextMaxConcurrent { get; set; } = 1;
    /// <summary>Ardışık metin isteği BAŞLANGIÇLARI arasında en az bekleme (ms). 0 = boşluk yok.</summary>
    public int TextMinIntervalMs { get; set; } = 1500;
    /// <summary>429/5xx (rate-limit/geçici) durumunda kaç kez BEKLEYİP yeniden denensin (deneme hakkı YAKMADAN).</summary>
    public int TextMaxRetries { get; set; } = 4;
    /// <summary>KALICI kota hatası (insufficient_quota) alınınca metin üretimini kaç sn duraklatıp sonra bir kez daha denesin.</summary>
    public int TextQuotaCooldownSeconds { get; set; } = 300;
}
