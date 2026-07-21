# Worker Log Analizi — Stabilite ve Deploy Rehberi (2026-07-21)

Bu belge worker loglarındaki (worker20260720/21 + workererrors20260720/21) bulguları,
yapılan kod düzeltmelerini ve gönderimleri **kalıcı olarak stabil** hale getirmek için
atman gereken adımları içerir.

---

## 1. Telegram neden TAMAMEN durmuştu? (asıl sebep bulundu ve düzeltildi)

Worker `ScheduledDispatchJob` sonuçları çökmeyi net gösteriyor:

```
16:32  1/4 gönderildi
16:36  0/1
17:01  0/1
17:13  0/1
17:18  0/1
17:23  0/1
17:31  0/3
17:39  0/1  ...  19:13  0/1   → saatlerce 0/N (hiçbir planlı yayın gitmiyor)
```

Sebep: `TelegramPublisher.cs` içinde buton olmadığında (varsayılan davranış — detay
linki caption'a konuyor) JSON gövdesine `"reply_markup": null` yazılıyordu. Telegram bunu
**400 Bad Request: object expected as reply markup** ile reddediyor. Bu yüzden her gönderim
5 denemede `Failed`'a düşüyordu → "bir gönderiyor bir göndermiyor" ve sonunda tamamen durması.

**Düzeltme yapıldı** (`TelegramPublisher.cs`): null alanlar artık JSON'a hiç yazılmıyor
(`JsonSerializerOptions { DefaultIgnoreCondition = WhenWritingNull }`). Bu dosya bir önceki
turda diske yazıldı.

> ⚠️ Bu düzeltme **asıl gönderici Worker'dır**. Aşağıdaki 4. bölümdeki deploy yapılmadan
> Telegram düzelmez — çünkü çalışan servis eski derlemedir.

---

## 2. İçerik akışını kesen gizli DB hatası (93 hata — düzeltildi)

`workererrors` loglarında en çok tekrarlanan hata (93 kez):

```
String or binary data would be truncated in table 'editorial.content_audit', column 'ActorRef'.
String or binary data would be truncated in table 'editorial.content_items', column 'CreatedByRef'.
Truncated value: 'ingestion:https://news.google.com/rss/articles/CBMiuwFBVV95cUx...'
```

Sebep: `CreatedByRef` ve `ActorRef` kolonları **nvarchar(200)**. Google News RSS linkleri
400–800 karakter. Kod `createdByRef: "ingestion:{tam_url}"` yazınca kolon taşıyor,
`SaveChanges` patlıyor ve **haber hiç kaydedilmiyordu** — yani onay kuyruğuna düşmeyen,
sessizce kaybolan içerikler. Bu, gönderim sayısının neden düştüğünün ikinci büyük sebebidir.

**Düzeltme yapıldı** (`ContentDiscoveredHandler.cs`): Artık tam URL yerine kısa, anlamlı bir
referans yazılıyor → `ingestion:news.google.com`. Tam adres zaten `SourceUrl` kolonunda
(nvarchar(1000)) duruyor, tekrarı gereksizdi. Değer her zaman 200'ün çok altında → taşma biter.

### DB tarafında yapman gereken (opsiyonel ama önerilir)

Kod düzeltmesi tek başına yeterli — artık kolon taşmaz. Ekstra güvenlik istersen kolonları
genişletebilirsin (SSMS'te `ContentPlatform` veritabanında):

```sql
ALTER TABLE editorial.content_items ALTER COLUMN CreatedByRef nvarchar(1000) NOT NULL;
ALTER TABLE editorial.content_audit ALTER COLUMN ActorRef     nvarchar(1000) NOT NULL;
```

**Temizlik gerekmez:** Taşan kayıtlar zaten hiç yazılamadı (transaction geri alındı), yani
veritabanında yarım/bozuk satır yok. Sadece o haberler atlandı; kaynaklar tekrar taranınca
yeni kodla normal kaydedilecekler.

---

## 3. Refresh token sistemi — ZATEN VAR ve çalışıyor

İstediğin refresh token altyapısı kodda **zaten kurulu ve loglarda çalışır durumda**:

| Kanal | Mekanizma | Durum |
|-------|-----------|-------|
| **X (Twitter)** | Her gönderimde access token yenilenir; refresh token TEK KULLANIMLIK olduğu için rotasyon + hesap bazlı kilit + anında DB'ye yazma (`XPublisher.GetValidAccessTokenAsync`) | ✅ Sağlam |
| **YouTube** | Her gönderimde `refresh_token` → taze `access_token` değişimi (`YoutubePublisher`) | ✅ Sağlam |
| **Instagram / Threads / TikTok** | Günlük `TokenRefreshJob` → süresi 7 günden az kalan uzun ömürlü token'ları gerçek API ile yeniler (`TokenRefresher`), 60 günlük token'lar süresiz yaşar | ✅ Çalışıyor |

Log kanıtı:
```
[INF] Adding job: DEFAULT.TokenRefreshJob
[INF] TokenRefreshJob: 0 hesap işlendi.   → şu an yenilenmesi gereken hesap yok (normal)
```

"0 hesap işlendi" = **hata değil**; hiçbir token 7 gün eşiğine girmemiş demek. Bağlama anında
(`MetaOAuthService`) 60 günlük bitiş tarihi doğru kaydediliyor, iş de her gün kontrol ediyor.

**Yani yeni bir refresh sistemi kurmana gerek yok.** Token'lar öldüyse sebep sistemin
yokluğu değil; genelde şu ikisidir:
- **YouTube:** Google Cloud OAuth uygulaması **"Testing"** modundaysa refresh token 7 günde
  iptal olur. Kalıcı çözüm: uygulamayı **"In production"** yap, sonra kanalı panelden bir kez
  yeniden bağla. (Kod bu durumu zaten `invalid_grant` mesajıyla açıkça bildiriyor.)
- **Instagram:** 60 günden uzun süre hiç gönderim/yenileme olmadıysa token ölür. `TokenRefreshJob`
  her gün çalıştığı sürece bu olmaz — **worker servisinin kesintisiz ayakta** olması şart (bkz. 5. bölüm).

---

## 4. DEPLOY — düzeltmelerin devreye girmesi için (EN ÖNEMLİ ADIM)

Çalışan worker `C:\Services\TelegramWorker\` altında ve **eski derleme**. Stack trace'lerde
hâlâ `C:\Users\User\Desktop\Github\TelegramBot` yolları görünüyor — yani prod servis, bu
makinedeki (`D:\projeler\TelegramBot`) güncel kaynaktan derlenmemiş. Düzeltmeler ancak yeniden
derleyip deploy edince etkir.

Adımlar (Windows, prod makinede):

```powershell
# 1) Servisi durdur
Stop-Service TelegramWorker      # ya da: sc stop TelegramWorker

# 2) Güncel kaynağı derle (Release)
cd D:\projeler\TelegramBot
dotnet publish src\Host\ContentPlatform.Worker -c Release -o C:\Services\TelegramWorker

# API de aynı düzeltmeleri paylaşıyor → onu da yayınla
dotnet publish src\Host\ContentPlatform.Api -c Release -o C:\Services\ContentPlatformApi

# 3) Servisi başlat
Start-Service TelegramWorker

# 4) İlk dakikalarda logu izle: "ScheduledDispatchJob: N/N gönderildi" (0/N DEĞİL) görmelisin
```

> Not: Worker ve API aynı `Publishing` projesini paylaşır; Telegram düzeltmesi ikisinde de
> geçerli. İkisini de yayınla.

---

## 5. Gönderimleri KALICI stabil tutmak için kontrol listesi

1. **Worker servisi restart'ta otomatik açılsın.** Loglarda 16:45–16:54 ve 19:13–00:40 arası
   uzun boşluklar var → servis durmuş/çökmüş. Bu boşluklarda `TokenRefreshJob` ve
   `ScheduledDispatchJob` de durur.
   ```powershell
   sc failure TelegramWorker reset= 86400 actions= restart/5000/restart/5000/restart/5000
   sc config TelegramWorker start= auto
   ```

2. **API sağlık kontrolü + otomatik restart** (aynı şekilde) — API 500'leri ağ kesintisinde
   birikiyordu.

3. **YouTube kotası:** Varsayılan ~6 video/gün. Kadansı buna göre ayarla ya da Google'dan kota
   artırımı iste; yoksa her gün belli saatten sonra "kota doldu" ile Failed birikir.

4. **Instagram hız limiti** ("User is performing too many actions"): Aynı hesaba kısa aralıkla
   çok gönderim yapma; kadansa 30–45 dk aralık koy.

5. **Bot token'ı loglarda görünüyor** (INF HttpClient satırlarında URL içinde). Bu log dosyalarını
   paylaştığın için **token'ı BotFather'dan yenile** ve `System.Net.Http.HttpClient` log seviyesini
   `Warning`'e çek (appsettings `Logging` bölümü).

6. **Başarısız yayınları yeniden kuyruğa al:** Bir önceki turdaki `db_onarim_20260721.sql`
   betiğini (kod deploy edildikten SONRA) çalıştır — Telegram/kota/IG/ağ kurbanlarını nedenine
   göre yeniden planlar.

---

## Özet: yapılan kod düzeltmeleri

| Dosya | Sorun | Durum |
|-------|-------|-------|
| `TelegramPublisher.cs` | reply_markup:null → 400, Telegram durdu | ✅ Düzeltildi (deploy bekliyor) |
| `SourceEndpoints.cs` | /sources/test işlenmeyen 500 | ✅ Düzeltildi (deploy bekliyor) |
| `ContentDiscoveredHandler.cs` | CreatedByRef/ActorRef taşması → 93 içerik kaybı | ✅ Düzeltildi (deploy bekliyor) |

**Sıradaki tek kritik adım: 4. bölümdeki deploy.** O yapılmadan hiçbir düzeltme prod'a geçmez.
