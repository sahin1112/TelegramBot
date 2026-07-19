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

    /// <summary>İletişim/KVKK e-postası. Gizlilik & Çerez politikası sayfalarında gösterilir.
    /// Boşsa alan adından türetilir (iletisim@alanadi). Panelden 'site.contact_email' ile düzenlenir.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Hakkımızda sayfası metni (düz metin; paragraflar boş satırla ayrılır).
    /// Panelden 'site.about_text' ile düzenlenir; boşsa otomatik tanıtım metni basılır.</summary>
    public string? AboutText { get; set; }

    // ---- Reklam (Monetization) ----
    /// <summary>Reklam slotları yalnız bu açıkken basılır (politika: varsayılan kapalı).</summary>
    public bool AdsEnabled { get; set; } = false;

    /// <summary>AdSense yayıncı kimliği (ör. "ca-pub-xxxxxxxx"). Boşsa slotlar boş kap olarak kalır.</summary>
    public string? AdSenseClient { get; set; }

    /// <summary>AdSense reklam birimi (ad unit) Slot ID'si — manuel reklam yerleşimleri için gerekli.
    /// BOŞSA manuel &lt;ins&gt; basılmaz; Otomatik reklamlar (Auto ads) devreye girer (panelde açılır).
    /// Slotsuz &lt;ins&gt; ASLA dolmaz, o yüzden slot yoksa hiç basmıyoruz.</summary>
    public string? AdSenseSlot { get; set; }

    // ---- Sosyal kanallar (ana sayfa şeridi) ----
    /// <summary>
    /// Ana sayfa "Sosyalde ..." şeridi ARTIK "Sosyal Hesaplar" bölümünden gelir (IPublicSocialProvider):
    /// hedeflerde "ana sayfada yayınla" seçili kanallar. BlogEndpoints doldurur. Doluysa alttaki
    /// TelegramUrl/XUrl... yedek alanları KULLANILMAZ (yalnız hiç seçim yoksa geriye-dönük gösterim).
    /// </summary>
    public IReadOnlyList<PublicSocialLink> HomeSocials { get; set; } = System.Array.Empty<PublicSocialLink>();

    // ---- Yedek sosyal alanlar (Site/SEO ayarları). Yalnız HomeSocials boşsa gösterilir. ----
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

    /// <summary>Üst menü — DİNAMİK olarak DB kategorilerinden doldurulur (BlogEndpoints). Boşsa NavCategories'e düşer.</summary>
    public IReadOnlyList<SiteNavItem> Nav { get; set; } = System.Array.Empty<SiteNavItem>();

    /// <summary>Kategori yoksa yedek üst menü etiketleri (etiket sayfalarına link).</summary>
    public IReadOnlyList<string> NavCategories { get; set; } = new[] { "Gündem", "Teknoloji", "Yaşam", "İş Dünyası", "Kültür", "Girişimcilik" };

    /// <summary>Hero sloganı (büyük başlık). Boşsa Description kullanılır.</summary>
    public string? HeroSlogan { get; set; }

    public string BaseUrlTrimmed => PublicBaseUrl.TrimEnd('/');
    public string Author => string.IsNullOrWhiteSpace(AuthorName) ? SiteName : AuthorName!;
    public string HeroHeadline => string.IsNullOrWhiteSpace(HeroSlogan) ? Description : HeroSlogan!;
}
