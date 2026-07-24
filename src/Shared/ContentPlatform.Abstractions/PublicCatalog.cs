namespace ContentPlatform.Abstractions;

/// <summary>
/// Public site (blog) için aktif kategori kataloğu. Platform bağlamı SAĞLAR (DB'deki kategoriler),
/// Site bağlamı TÜKETİR (üst menü + /kategori/{slug} sayfaları). Böylece menü ve kategori sayfaları
/// tamamen DİNAMİK olur — kodda sabit kategori listesi yoktur.
/// </summary>
public sealed record PublicCategory(Guid Id, string Name, string Slug);

public interface IPublicCategoryProvider
{
    /// <summary>Yalnız AKTİF kategoriler (ada göre sıralı). Yeni kategori panelden eklenince menüde belirir.</summary>
    Task<IReadOnlyList<PublicCategory>> GetActiveAsync(CancellationToken ct);
}

/// <summary>Bir kategorinin otomatik üretim ayarları (RSS içeriği için).</summary>
public sealed record CategoryAutomation(bool AutoContent, bool AutoImage, bool AutoVideo, bool AutoPublish,
    string Card1x1, string CardReels, bool AttentionBadges);

/// <summary>
/// Kategori otomasyon ayarlarını bağlamlar arasına açar. Platform SAĞLAR, Editorial TÜKETİR
/// (RSS keşfinde yeni içeriğe otomatik üret/yayınla niyeti uygulanır).
/// </summary>
public interface ICategoryAutomationProvider
{
    /// <summary>Kategori otomasyon ayarları; kategori yoksa null.</summary>
    Task<CategoryAutomation?> GetAsync(Guid categoryId, CancellationToken ct);
}

/// <summary>Üst menü / gezinme öğesi (etiket veya kategori sayfasına link).</summary>
public sealed record SiteNavItem(string Label, string Href);

/// <summary>
/// Ana sayfada gösterilecek bir sosyal kanal. Platform bağlamı SAĞLAR ("Sosyal Hesaplar" bölümünde
/// "ana sayfada yayınla" seçilmiş hedeflerden), Site bağlamı TÜKETİR (ana sayfa "Sosyalde ..." şeridi).
/// Aynı platformda birden çok kanal olabilir; yalnız seçilenler döner.
/// </summary>
public sealed record PublicSocialLink(Platform Platform, string Title, string Url, int? Followers);

public interface IPublicSocialProvider
{
    /// <summary>Yalnız "ana sayfada yayınla" seçili + aktif + herkese açık URL'i olan hedefler (platforma göre gruplu sıralı).</summary>
    Task<IReadOnlyList<PublicSocialLink>> GetHomeLinksAsync(CancellationToken ct);
}
