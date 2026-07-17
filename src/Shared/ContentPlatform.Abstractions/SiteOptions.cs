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

    /// <summary>Yazar/yayıncı adı (byline + JSON-LD). Boşsa SiteName kullanılır.</summary>
    public string? AuthorName { get; set; }

    // ---- Reklam (Monetization) ----
    /// <summary>Reklam slotları yalnız bu açıkken basılır (politika: varsayılan kapalı).</summary>
    public bool AdsEnabled { get; set; } = false;

    /// <summary>AdSense yayıncı kimliği (ör. "ca-pub-xxxxxxxx"). Boşsa slotlar boş kap olarak kalır.</summary>
    public string? AdSenseClient { get; set; }

    // ---- Sosyal kanallar (ana sayfa şeridi). Boş olanlar gösterilmez. ----
    public string? TelegramUrl { get; set; }
    public string? TelegramMembers { get; set; }
    public string? XUrl { get; set; }
    public string? XFollowers { get; set; }
    public string? InstagramUrl { get; set; }
    public string? InstagramFollowers { get; set; }
    public string? ThreadsUrl { get; set; }
    public string? ThreadsFollowers { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? YoutubeSubscribers { get; set; }

    // ---- SEO / Analytics / uyum ----
    public string? Ga4Id { get; set; }
    public string? GtmId { get; set; }
    public string? GscVerification { get; set; }
    public string? AdsTxt { get; set; }
    public bool ConsentRequired { get; set; }
    public string? PrivacyUrl { get; set; }
    public string? CookieUrl { get; set; }
    public string? IndexNowKey { get; set; }
    public int AdsMinWords { get; set; } = 400;

    public string BaseUrlTrimmed => PublicBaseUrl.TrimEnd('/');
    public string Author => string.IsNullOrWhiteSpace(AuthorName) ? SiteName : AuthorName!;
}
