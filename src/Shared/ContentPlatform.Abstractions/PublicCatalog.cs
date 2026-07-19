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
