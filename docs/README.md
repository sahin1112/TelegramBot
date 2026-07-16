# Proje Dokümantasyonu — Dizin

İçerik Otomasyon Platformu’nun tüm tasarım dokümanları. **Ana kaynak** `00-PROJE-Ana-Dokuman.md`’dir (her şeyin eksiksiz bulunduğu tekil doküman). Diğer dosyalar bu ana dokümanın konu odaklı, derinleştirilebilir parçalarıdır.

## Dosyalar

| Dosya | Kapsam |
|---|---|
| **00-PROJE-Ana-Dokuman.md** | **Ana proje dokümanı — eksiksiz her şey.** Tek doğruluk kaynağı; parçalarda bir değişiklik olursa buraya da işlenir. |
| 01-Mimari.md | Mimari felsefe, teknoloji yığını, temiz kod ilkeleri, bağlamlar, klasör yapısı, ortak desenler (Outbox, durum makinesi, idempotency), örneklerle. |
| 02-API-ve-Versiyonlama.md | Kendi API tasarımı (public blog + admin), sürümleme stratejisi; dış API istemcileri (OpenAI, Telegram, X, IG, Threads, AdsGram) ve sürüm sabitleme. |
| 03-Entegrasyonlar-ve-Arayuz.md | “Tüm entegrasyonlar arayüzden.” Bağlantı bilgisi veri modeli (SocialAccount, PublicationTarget, NetworkIntegrations), şifreleme, token yenileme, bağlantı tabloları ve akışları. |
| 04-Admin-Paneli.md | Admin panelinin tüm ekranları, roller (RBAC-lite), onay/taslak/görsel-bekleyen akışları, ayarlar, feature flag. |
| 05-Blog-ve-SEO.md | Blog (SSR), SEO (etiket sayfaları, iç linkleme, structured data, IndexNow), yorum/moderasyon, reklam yerleşimi + uygunluk + CMP/ads.txt. |
| 06-Telegram.md | Tüm Telegram: yayın (kanal+tartışma grubu), grup yönetimi (link/aktiflik/rozet/liderlik), admin grubu→taslak, dört reklam türü, pacing. |
| 07-X.md | X (Twitter) entegrasyonu: OAuth 1.0a, medya upload + tweet, linksiz-ana + reply-link stratejisi, limitler, kimlik alanları. |
| 08-Instagram.md | Instagram Graph API: Business/Creator + FB Page, uzun-ömürlü token + yenileme, yayın akışı (container→publish), limitler. |
| 09-Threads.md | Threads API: kimlik, token, yayın akışı, limitler. |

## Önerilen çalışma sırası (sıralı geliştirme)

1. **01-Mimari** — iskelet, bağlamlar, temiz kod, veri modeli çekirdeği.
2. **03-Entegrasyonlar-ve-Arayuz + 02-API** — bağlantı tabloları, kimlik/şifreleme, token yenileme, dış API sürümleri.
3. **04-Admin-Paneli** — panel ekranları ve akışları.
4. **05-Blog-ve-SEO** — public site.
5. **06-Telegram** — ilk yayın kanalı (MVP).
6. **07-X → 09-Threads → 08-Instagram** — zorluk sırasına göre sosyal adaptörler.

> Her parça derinleştirildikçe **00-PROJE-Ana-Dokuman.md** ile tutarlı tutulur. Ana doküman v6 seviyesindedir (Monetization/Promotion ayrımı, görsel 3. seçenek dahil).
