# 05 — Blog ve SEO

> Blog (SSR), SEO (birincil öncelik), yorum/moderasyon ve blog reklamları (uygunluk + yerleşim + uyum). Sıralı geliştirmede derinleştirilecek. Tam bağlam: `00-PROJE-Ana-Dokuman.md`.

## 1. Blog temeli
- **SSR şart** (Razor Pages/MVC veya Next.js) — saf CSR SEO’yu zayıflatır.
- **URL:** kategori alt-dizin (`site.com/kripto/haber-slug`); subdomain yalnız kategoriler ayrı markaysa. Hiyerarşi: **Site → Category → Content**.
- **Sade & hızlı tema**, mobil öncelikli.

## 2. Veri
`BlogPost: Id, ContentRevisionId, PrimaryCategoryId, Slug(UNIQUE), Title, MetaDescription, BodyHtml, CoverImageUrl, CanonicalUrl, JsonLd, PublishedAt, Views, CommentsEnabled` (+ reklam uygunluk bayrakları §5).

## 3. SEO (birincil)
- **On-page:** benzersiz `Title`/`MetaDescription`(=çok kısa açıklama), canonical, Open Graph/Twitter Card, **JSON-LD Article + BreadcrumbList**, H1/H2 hiyerarşisi, slug=`PrimaryKeyword`, görsel `alt`(=`ImageAltText`).
- **İçerik etiketleri → SEO iniş sayfaları:** her `Tag` için `/etiket/<tag>` (topical silo); AI üretir. Sosyalde hashtag, blogda iniş sayfası.
- **Otomatik iç linkleme:** ortak etiket/benzerlik (embedding) ile ilgili yazılara link.
- **Yazar/editör profilleri (E-E-A-T):** yazar/editör/uzmanlık/bio/sosyal + “AI desteği kullanıldı mı?”.
- **İçerik şablonları:** son dakika/karşılaştırma/liste/nasıl-yapılır/günlük özet/rapor/duyuru/sponsorlu.
- **İçerik yenileme kuyruğu:** bozuk link/eski fiyat/geçmiş tarih/trafik kaybı → “güncellenmeli”.

### Keşfe bildirim (doğru ifade)
Yeni URL’ler **sitemap + robots.txt + Search Console + destekleyen motorlar için IndexNow (Bing/Yandex vb.)** ile bildirilir. **İndeksleme garanti değildir; “anında indeksleme” denmez; indeks durumu izlenir.** (Google eski sitemap-ping uç noktasını kaldırdı; Google IndexNow kullanmaz; Indexing API genel makale için değil.) **FAQ/HowTo** zengin sonuçları Google’da kaldırıldığından schema “zengin sonuç getirir” vaadiyle konmaz.

### Teknik SEO
`sitemap.xml` (post+kategori+etiket), `robots.txt`, webp+lazy-load+CDN, Core Web Vitals, blog RSS, güçlü arama (PG full-text → semantik).

## 4. Yorumlar (moderasyonlu)
Ziyaretçi yorum → `Pending` → admin onayıyla yayınlanır. `Comment: Id, BlogPostId, AuthorName, AuthorEmail?, Body(sanitize), Status(Pending|Approved|Rejected|Spam), IpHash, CreatedAt, ModeratedAt?`. HtmlSanitizer + hız limiti + IP hash. KVKK/GDPR: gizlilik politikası, çerez onayı, saklama süresi.

## 5. Blog reklamları (Monetization — bkz. 00 §14)
- **`MonetizationEligibility`** (KRİTİK): otomatik içerik + AdSense riski. Her yazıya otomatik reklam açılmaz. Per-post: `MonetizationEligibility(Pending|Eligible|Restricted|NotEligible|ManualReviewRequired)` + `CanShowProgrammaticAds, CanShowDirectAds, CanShowAffiliateLinks, SensitiveContent, AdExclusionReason`. Reklam almayacaklar: kısa RSS özeti, yeterince değiştirilmemiş kaynak, doğrulanmamış haber, düzeltme/hata sürecindeki, yasal riskli, yalnız görsel+birkaç cümle, yardımcı sayfalar.
- **Teşvikli trafik yasağı + `TrafficQuality` (KRİTİK):** AdSense sayfalarına ziyaret/tık karşılığı puan/rozet/ödül **verilmez**; TrueNetwork/topluluk görevleri reklamlı sayfalara yönlendirilmez. Şüpheli trafikte sayfa açılır ama reklam kodu çalışmaz.
- **`AdPlacement`** (kod içinde sabit değil): `Header, BelowIntroduction, InArticle1/2, Sidebar, AfterArticle, RelatedContent, CategoryPage, Homepage, MobileAnchor, DesktopRail`; her biri aktif/pasif, mobil/masaüstü, min içerik uzunluğu, max reklam, izinli sağlayıcı, kategori/hassas engel, A/B.
- **CMP / gizlilik / ads.txt** (Site alanları): `AdsTxtContent, CmpProvider, ConsentMode, PersonalizedAdsEnabled, PrivacyPolicyUrl, CookiePolicyUrl, AdSensePublisherId, AdSenseStatus, LastPolicyCheckAt`. AEA/BK/İsviçre’de sertifikalı CMP zorunlu; ads.txt önerilir.
- **Sponsorlu makale vs Affiliate ayrı** (`SponsoredContent` — “Sponsorlu İçerik” etiketi + `rel="sponsored"`; `AffiliateProgram/AffiliateConversion`).

---
*Derinleştirilecek:* tema/şablon HTML iskeletleri, structured data örnekleri, sitemap/IndexNow job’u, AdPlacement render kuralları, CMP entegrasyon adımları.
