namespace ContentPlatform.Abstractions;

/// <summary>
/// Public site (blog) ayarları. `Site` yapılandırma bölümünden bağlanır.
/// Editorial linki üretmek, Site ise render/kanonik URL için kullanır.
/// </summary>
public sealed class SiteOptions
{
    /// <summary>Blogun genel adresi (sonunda / olmadan). Ör. https://ornek.com</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Site/marka adı (başlık ve OG için).</summary>
    public string SiteName { get; set; } = "İçerik";

    /// <summary>Kısa açıklama (ana sayfa meta/OG için).</summary>
    public string Description { get; set; } = "Güncel içerikler";

    public string BaseUrlTrimmed => PublicBaseUrl.TrimEnd('/');
}
