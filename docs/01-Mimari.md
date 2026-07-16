# 01 — Mimari, Teknoloji ve Temiz Kod

> Bu doküman mimarinin çekirdeğidir: felsefe, teknoloji, bağlamlar, klasör yapısı, temiz kod ilkeleri ve ortak desenler. Ayrıntı derinleştirmesi (sınıf sınıf imzalar, örnek kodlar) sıralı geliştirmede burada büyütülecek. Tam bağlam: `00-PROJE-Ana-Dokuman.md`.

## 1. Mimari yaklaşım
**Modular Monolith + Clean Architecture.** Tek çözüm, tek deploy; modüller **beş bağlam (bounded context)** altında gruplanır. Mikroservis değil — tek kişi/küçük ekip, tek DB, kolay hata ayıklama. Modülerlik ileride bir parçayı servise çıkarma opsiyonunu korur.

### Bağlamlar
| Bağlam | Modüller |
|---|---|
| **Ingestion** | Sources, FactPack çıkarımı, dedup |
| **Editorial** | Content, ContentRevision, GenerationRun, QualityGate, Media |
| **Publishing** | DistributionPlan, PublicationTarget, Channels, Ads |
| **Site** | Blog, Seo, Comments |
| **Platform** | Sites, SocialAccounts+Token, Community, AdminInbox, Audit, Notifications, Admin |

Bağlamlar arası iletişim: doğrudan referans yerine `*.Contracts` projeleri + **Integration Event**’ler. Başlangıçta bağlam başına tek `DbContext` yeterli.

### Katmanlama (her modül)
```
Domain/           → Entity, enum, değer nesnesi (saf, bağımsız)
Application/      → iş kuralları, servisler, repo arayüzleri, DTO
Infrastructure/   → EF Core DbContext, repo impl, dış servis çağrıları, Migrations/
Api/              → Minimal API endpoint'leri
<Ad>Module.cs     → DI kaydı + endpoint map
```

## 2. Teknoloji yığını
`.NET 9 (net9.0)` · ASP.NET Core Minimal API · EF Core 9 · **SQL Server 2022** · **Redis** (kuyruk/sayaç/rate-limit) · **Quartz.NET** · **SkiaSharp** (kart render) · OpenAI (metin+görsel, konfigüre `ModelCatalog`) · **HtmlSanitizer** · **ASP.NET Data Protection/KMS** (token şifreleme) · Admin **SPA** (`wwwroot/admin`) · Blog **SSR** (Razor/MVC veya Next.js) · **Object storage (S3)+CDN** · **OpenTelemetry**. Merkezi paket sürümü: `Directory.Packages.props`.

## 3. Klasör yapısı
```
src/
  Host/ ContentPlatform.Api/ (blog SSR + admin API + SPA)  ContentPlatform.Worker/ (Quartz)
  Contexts/
    Ingestion/ {Sources, FactPack}
    Editorial/ {Content, Generation, Quality, Media}
    Publishing/ {Distribution, Targets, Channels, Ads}
    Site/ {Blog, Seo, Comments}
    Platform/ {Sites, SocialAccounts, Community, AdminInbox, Audit, Notifications, Admin}
  Shared/  Shared.Contracts/
  Assets/  (font, logo, kart şablonları)
tests/
```

## 4. Temiz kod ilkeleri
- **SOLID** — özellikle bağımlılıkların tersine çevrilmesi: Application katmanı arayüzleri tanımlar, Infrastructure uygular.
- **Adaptör deseni** — her yayın kanalı bir `IChannelPublisher`; her AI sağlayıcı `ITextGenerationProvider`/`IImageGenerationProvider`. Çekirdek, sağlayıcıyı bilmez.
- **Bağımlılık enjeksiyonu** — her şey DI ile; `<Ad>Module.cs` modülün kompozisyon kökü.
- **Kimlik bilgisi = veri, config değil** — token’lar şifreli DB’de, adaptöre **çağrıda** verilir (global okuma yok).
- **Saf Domain** — Entity’ler dış bağımlılık taşımaz; iş kuralı Application’da.
- **Result/Either tipleri** — hata akışı exception’a değil, açık sonuç tiplerine dayanır (öngörülebilir).
- **Idempotency & saf fonksiyonlar** — yayın/dağıtım tekrar edilebilir; yan etkiler sınırlı.

## 5. Ortak desenler
- **Durum makinesi** — içerik EditorialStatus/MediaStatus/PublicationStatus eksenlerinde ilerler (ayrıntı: 00 §6).
- **Outbox + retry + DeliveryAttempt** — çok kanallı dağıtım güvenilir; “istek gitti cevap gelmedi” durumunda körlemesine tekrar yok (idempotency).
- **DistributionPlan** — “ne nereye” tek doğruluk kaynağı; çok-kategori hedef tekilleştirmesi.
- **FactPack + QualityGate** — kaynak/olgu kaydı + değer/risk kapısı (uydurma ve düşük değerli içeriğe karşı).
- **ModelCatalog + ModelRoutingPolicy** — model/fiyat kodda sabit değil; iş başına model seçimi.
- **Feature flag + granular kill-switch** — güvenli açılım ve acil fren.

## 6. Performans & ölçek
Asenkron uçtan uca; Redis kuyruk; AI/asset/sıcak sayfa cache (CDN + output cache); platform rate-limit farkındalığı; tek render → çok boyut webp; Worker yatay ölçeklenebilir.

## 7. Güvenlik (özet — ayrıntı 05/03)
Token şifreleme (Data Protection/KMS), admin auth + IP kısıt + opsiyonel 2FA, SSRF sertleştirme (URL çekmede yerel/metadata IP engeli, redirect sınırı, MIME allowlist), geniş HTML sanitizasyon (yorum + içe aktarılan + AI HTML), KVKK/GDPR (saklama + anonimleştirme).

## 8. Observability
OpenTelemetry + TraceId, job run history, dead-letter queue, platform hata oranı, kuyruk yaşı, kaynak sağlık skoru, token yenileme başarısı, içerik başına gerçek maliyet.

---
*Derinleştirilecek:* her bağlam için sınıf/arayüz imzaları, DI kayıt örnekleri, örnek endpoint iskeletleri, migration stratejisi, test yaklaşımı.
