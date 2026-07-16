# 04 — Admin Paneli

> Panelin tüm ekranları, roller, akışlar. `wwwroot/admin` SPA + `/api/admin/*`. Sıralı geliştirmede derinleştirilecek. Tam bağlam: `00-PROJE-Ana-Dokuman.md`.

## 1. Roller (RBAC-lite → tam)
MVP’de tek kullanıcı, ama model rol taşır: `Owner, Admin, Editor, Reviewer, Moderator, Advertiser, Analyst, ReadOnly`. Kategori/işlem bazlı yetki; **moderatör sosyal token’ları göremez.** Tam RBAC sonraki fazda.

## 2. Ekranlar

| Ekran | İçerik |
|---|---|
| **Dashboard** | Onay bekleyen sayısı, bugün üretilen/yayınlanan, hata, **bu ay AI harcaması** (istatistik), reklam geliri. |
| **Onay Kuyruğu** | RSS/sayfa ham içeriği (AI’sız önizleme: başlık/özet/link); satırda **görsel kaynağı seçimi (AI / SkiaCard / Ben yükleyeceğim)** + **Onayla / Reddet** + **toplu onay**. Onaylanınca AI çalışır. |
| **Taslaklar** | Admin grubu/AI taslakları (`ReviewDraft`): **hızlı düzenle + “yeniden üret” + onayla**. |
| **Görsel Bekleyenler** | `MediaStatus=AwaitingManualUpload` içerikler — görsel yükle → sıraya girer. (“Ben yükleyeceğim” akışı.) |
| **İçerik Ekle** | Manuel: (a) **AI ile** yapıştır → taslak; (b) **AI’sız** bitmiş metin+görsel → doğrudan sıra. Çok-kategori + görsel kaynağı seçimi. |
| **İçerikler** | Üretilenler; önizle, geri çek, yeniden dene, **zaman çizelgesi/geçmiş** (kim/nereden/ne zaman/nereye), düzeltme. |
| **Dağıtım Önizleme** | `DistributionPlan`: ne nereye gidecek, hangi metin/görsel/saat, hedef tekilleştirme; onayla. |
| **Siteler/Markalar** | Site CRUD + **CMP/ads.txt/AdSense uyum** alanları (bkz. 05). |
| **Kategoriler** | CRUD; görsel modu + `DefaultImageSource`, dil, hashtag havuzu, reklam frekansı, RSS-oto-onay istisnası, risk seviyesi. |
| **Kaynaklar** | RSS/sayfa ekle; tarama sıklığı; **telif/robots** durumu; selector test/önizleme; kaynak sağlık skoru. |
| **Sosyal Hesaplar** | Hesap oluştur (platform+kimlik, şifreli); **IG/Threads token son kullanımı**; kategorilere bağla; sağlık. |
| **Hedefler** | Grup/kanal/profil (`PublicationTarget`); capabilities, karakter limiti, medya gereksinimi. |
| **Telegram Grupları** | Link auto-sil + whitelist; reaksiyon sayımı; haftalık liderlik; kanal→tartışma grubu bağlama. |
| **Rozetler** | Grup başına rozet listesi (ad, metrik, eşik, native title yalnız admin). |
| **Admin Grubu** | Kapalı admin grubu + botu; izinli üyeler (`AllowedUserIds`). |
| **Monetization** | Advertisers, Inventory, DirectCampaigns, NetworkIntegrations, RevenueLedger, Payouts. |
| **Promotion** | AcquisitionCampaigns, Budgets, Creatives, Targeting, Metrics. |
| **Reklam Uygunluğu** | `MonetizationEligibility`, `AdPlacement`, `TrafficQuality`. |
| **Blog** | Yazı/slug/etiket sayfaları; içerik yenileme kuyruğu; şablonlar; yazar profilleri. |
| **Yorum Moderasyonu** | Bekleyen yorumlar → onayla/reddet/spam. |
| **Dead-letter / Kurtarma** | Başarısız işler: yeniden dene / düzenle-dene / atla / başka hesaptan yayınla. |
| **Maliyet** | ModelCatalog + aylık/kategorilik harcama istatistiği (tavan yok). |
| **Acil Durdurma** | Granular kill-switch: global + platform/hesap/kategori/AI/reklam. |
| **Feature Flags** | Yeni prompt/adaptör/şablon/oto-onay kategori/hesap bazında aç-kapa. |
| **Ayarlar** | OpenAI, Adsgram, admin Telegram alert kanalı, Data Protection, limitler. |
| **Bildirimler / Observability** | Hata geçmişi, hesap/token uyarıları, retry, kuyruk yaşı, platform hata oranı. |

## 3. Anahtar akışlar
- **Onay → AI:** ham içerik onaya düşer; onaylanınca (görsel kaynağı seçimiyle) AI üretir. “Ben yükleyeceğim” ise görsel gelene kadar sırada bekletmez, **Görsel Bekleyenler**’de tutar.
- **Test/önizleme:** yeni hesap/şablon önce **TestMode** (test kanalı/simülasyon) + platform önizleme (karakter aşımı, kesim, görsel oranı, hashtag, link önizleme, hedef).
- **Risk bazlı onay:** yüksek riskli içerik otomatik onaylanmaz; `ApprovalPolicy` çok adımlı olabilir.

---
*Derinleştirilecek:* ekran ekran alan/etkileşim ayrıntısı, yetki matrisi, SPA bileşen yapısı, endpoint eşlemesi, önizleme render mantığı.
