# 03 — Entegrasyonlar ve Arayüzden Yönetim

> **Temel ilke: tüm entegrasyonlar arayüzden yönetilir; kimlik bilgileri config değil, veridir.** Bu doküman bağlantı bilgisi veri modelini, bağlantı tablolarını, şifrelemeyi, token yenilemeyi ve “hesap oluştur → kategoriye bağla” akışlarını içerir. Sıralı geliştirmede ilk kurulacak veri katmanlarından. Tam bağlam: `00-PROJE-Ana-Dokuman.md`.

## 1. İlke
TrueNetwork’te sosyal kimlik bilgileri `appsettings`/ENV’de statikti. Burada **panelden** yönetilir: önce “Sosyal Hesap Oluştur” (platform + kimlik gir, şifreli sakla), sonra kategoriye/hedefe bağla. Aynı hesap birden çok kategoriye; bir kategori birden çok hesaba (**çoka-çok**). Adaptörler kimliği **çağrıda** alır.

## 2. Bağlantı tabloları (veri modeli)

### SocialAccount (kimlik/hesap)
`Id, SiteId?, Platform(Telegram|X|Instagram|Threads), DisplayName, CredentialsEncrypted(JSON), TokenExpiresAt?, Status(Active|Error|Disabled), LastError?, LastCheckedAt?`

Platforma göre şifreli `Credentials` alanları:
| Platform | Alanlar |
|---|---|
| Telegram | `BotToken` (+ hedefler `PublicationTarget`’ta) |
| X | `ApiKey, ApiSecret, AccessToken, AccessTokenSecret` (OAuth 1.0a) |
| Instagram | `IgUserId, PageId, LongLivedToken` (+`TokenExpiresAt`) |
| Threads | `ThreadsUserId, AccessToken` (+`TokenExpiresAt`) |

### PublicationTarget (fiziksel hedef)
Bir hesap onlarca hedefe yayabilir: `Id, SocialAccountId, Platform, ExternalTargetId, TargetType(Group|Channel|Profile|Feed), Language?, TimeZone?, IsActive, Capabilities[], CharacterLimit, MediaRequirements`.

### CategoryAccount (bağlama + zamanlama)
`CategoryId, SocialAccountId, SchedulePolicyId, IsEnabled` — hangi kategori hangi hesapla, hangi zamanlama politikasıyla.

### Reklam/gelir entegrasyonları (bkz. 06/05)
`NetworkIntegrations` (dış ağlar): `AdsGramChannelIntegration`, `TelegramBotAdIntegration`, `MiniAppAdIntegration`, `TelegramOfficialRevenue`, `AdSenseStatus` (Site’ta). Bunlar **kendi kampanya tablomuzdan ayrı** (para kazandığımız taraf).

## 3. Kimlik güvenliği
- **Şifreleme:** `CredentialsEncrypted` → ASP.NET Data Protection (anahtar ring ENV/KMS ile korunur) veya bulut KMS.
- **Maskeleme:** panelde token asla düz görünmez.
- **En az yetki:** RBAC’te moderatör token göremez (bkz. 04).

## 4. Token yenileme (çekirdek)
Instagram/Threads uzun-ömürlü token ~60 günde dolar; **otomatik yenilenmezse yayın sessizce durur.**
- **Hesap ekleme ekranında** uzun-ömürlü token istenir, `TokenExpiresAt` hesaplanır ve gösterilir.
- **`TokenRefreshJob` (günlük):** süresi yaklaşan (<7 gün) token’ları API’den yeniler, yeni son kullanımı yazar.
- **Yenilenemezse:** hesap `Status=Error` + admin’e Telegram bildirimi (“yeniden yetkilendir”).

## 5. Arayüzden akışlar
1. **Sosyal Hesap Oluştur:** platform seç → kimlik alanlarını gir → (IG/Threads’te uzun-ömürlü token + son kullanım) → kaydet (şifreli).
2. **Hedef Ekle:** hesaba grup/kanal/profil bağla (`ExternalTargetId`, tür, capabilities).
3. **Kategoriye Bağla:** kategori ↔ hesap seç + zamanlama politikası.
4. **Sağlık:** `AccountHealthJob` token/hesap durumunu düzenli kontrol eder; hata panelde + bildirimde.

## 6. Sağlayıcı soyutlama (AI + kanal)
- Kanal: `IChannelPublisher` (Telegram/X/IG/Threads/Blog).
- AI: `ITextGenerationProvider`, `IImageGenerationProvider`, `IEmbeddingProvider`, `IModerationProvider`.
Yeni entegrasyon = yeni adaptör + panelde bağlantı kaydı; çekirdek değişmez.

---
*Derinleştirilecek:* her platform için tam kimlik alanı + scope listesi, OAuth akış diyagramları, şifreleme anahtar yönetimi ayrıntısı, bağlantı doğrulama/test uçları, entegrasyon durum makinesi.
