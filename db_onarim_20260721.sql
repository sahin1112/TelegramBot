-- =====================================================================
-- ContentPlatform veritabanı onarım betiği — 2026-07-21
-- Kaynak: api20260720/21 + apierrors20260720/21 log analizi
--
-- KULLANIM: SSMS (veya Azure Data Studio) ile localhost,1433 üzerindeki
-- ContentPlatform veritabanına bağlanın ve adımları SIRAYLA, her adımın
-- çıktısını kontrol ederek çalıştırın. UPDATE'lerden önce ADIM 1'deki
-- yedek tablosu mutlaka alınmış olsun.
-- =====================================================================

USE ContentPlatform;
GO

-- =====================================================================
-- ADIM 1 — YEDEK: Başarısız yayınların kopyasını al (geri dönüş için)
-- =====================================================================
IF OBJECT_ID('publishing.publications_yedek_20260721') IS NULL
    SELECT *
    INTO publishing.publications_yedek_20260721
    FROM publishing.publications
    WHERE Status = 'Failed';
GO

-- =====================================================================
-- ADIM 2 — TESPİT: Başarısız yayınları nedenlerine göre listele
-- =====================================================================
SELECT Id, Channel, TargetRef, Attempts, ScheduledAt, UpdatedAt, Error
FROM publishing.publications
WHERE Status = 'Failed'
ORDER BY Channel, UpdatedAt DESC;
GO

-- =====================================================================
-- ADIM 3 — TELEGRAM "reply markup" kurbanları → yeniden kuyruğa al
-- Bu kayıtlar koddaki gerçek bir bug yüzünden düştü ("reply_markup": null
-- gönderiliyordu). Kod düzeltmesi (TelegramPublisher.cs) DEPLOY EDİLDİKTEN
-- SONRA bu adımı çalıştırın; yoksa yine 5 denemede tekrar Failed olurlar.
-- =====================================================================
UPDATE publishing.publications
SET Status      = 'Scheduled',
    Attempts    = 0,
    Error       = NULL,
    ScheduledAt = SYSDATETIMEOFFSET(),
    UpdatedAt   = SYSDATETIMEOFFSET()
WHERE Status = 'Failed'
  AND Channel = 'Telegram'
  AND Error LIKE '%reply markup%';
GO

-- =====================================================================
-- ADIM 4 — YOUTUBE kota kurbanları → kota sıfırlandıktan sonraya planla
-- "The user has exceeded the number of videos they may upload" — günlük
-- yükleme kotası doldu. Kota Pasifik gece yarısında sıfırlanır
-- (Türkiye saatiyle ~10:00). Yarın 10:30 TR'ye planlıyoruz; kayıtları
-- 30'ar dakika arayla sıralıyoruz ki kota tekrar tek seferde dolmasın.
-- =====================================================================
;WITH kota AS (
    SELECT Id,
           ROW_NUMBER() OVER (ORDER BY UpdatedAt) AS rn
    FROM publishing.publications
    WHERE Status = 'Failed'
      AND Channel = 'Youtube'
      AND (Error LIKE '%kota%' OR Error LIKE '%exceeded the number of videos%')
)
UPDATE p
SET Status      = 'Scheduled',
    Attempts    = 0,
    Error       = NULL,
    -- yarın 10:30 TR + (sıra-1)*30 dk
    ScheduledAt = DATEADD(MINUTE, (k.rn - 1) * 30,
                    TODATETIMEOFFSET(DATEADD(HOUR, 10, DATEADD(MINUTE, 30, CONVERT(datetime2, CONVERT(date, DATEADD(DAY, 1, SYSDATETIME()))))), '+03:00')),
    UpdatedAt   = SYSDATETIMEOFFSET()
FROM publishing.publications p
JOIN kota k ON k.Id = p.Id;
GO

-- =====================================================================
-- ADIM 5 — INSTAGRAM hız limiti kurbanları → aralıklı yeniden kuyruğa al
-- "User is performing too many actions" — IG hesabı kısa sürede çok işlem
-- yaptı. 2 saat sonradan başlayarak 45'er dakika arayla yeniden planla.
-- =====================================================================
;WITH ig AS (
    SELECT Id,
           ROW_NUMBER() OVER (ORDER BY UpdatedAt) AS rn
    FROM publishing.publications
    WHERE Status = 'Failed'
      AND Channel = 'Instagram'
      AND (Error LIKE '%too many actions%' OR Error LIKE '%istek hatası%')
)
UPDATE p
SET Status      = 'Scheduled',
    Attempts    = 0,
    Error       = NULL,
    ScheduledAt = DATEADD(MINUTE, 120 + (i.rn - 1) * 45, SYSDATETIMEOFFSET()),
    UpdatedAt   = SYSDATETIMEOFFSET()
FROM publishing.publications p
JOIN ig i ON i.Id = p.Id;
GO

-- NOT: "Instagram videoyu işleyemedi: {status_code:ERROR}" hatası alan
-- kayıt(lar) VİDEO FORMATI sorunudur (codec/süre/çözünürlük IG şartlarına
-- uymuyor olabilir) — bunları yeniden kuyruğa almadan önce videoyu kontrol
-- edin. Görmek için:
SELECT Id, TargetRef, Error, UpdatedAt
FROM publishing.publications
WHERE Status = 'Failed' AND Channel = 'Instagram' AND Error LIKE '%işleyemedi%';
GO

-- =====================================================================
-- ADIM 6 — GEÇİCİ AĞ HATASI kurbanları (YouTube oauth timeout, X/Telegram
-- timeout vb.) → hemen yeniden kuyruğa al. 20.07 17:00-17:30 arasında
-- sunucunun dış ağ erişiminde kesinti vardı; bunlar kalıcı hata değil.
-- =====================================================================
UPDATE publishing.publications
SET Status      = 'Scheduled',
    Attempts    = 0,
    Error       = NULL,
    ScheduledAt = SYSDATETIMEOFFSET(),
    UpdatedAt   = SYSDATETIMEOFFSET()
WHERE Status = 'Failed'
  AND (Error LIKE '%connection attempt failed%'
       OR Error LIKE '%task was canceled%'
       OR Error LIKE '%timeout%'
       OR Error LIKE '%zaman aşımı%')
  AND Error NOT LIKE '%reply markup%'        -- Adım 3'te işlendi
  AND Error NOT LIKE '%kota%'                -- Adım 4'te işlendi
  AND Error NOT LIKE '%too many actions%';   -- Adım 5'te işlendi
GO

-- =====================================================================
-- ADIM 7 — KONTROL: Failed kalan var mı? Scheduled kuyruk nasıl görünüyor?
-- =====================================================================
SELECT Status, Channel, COUNT(*) AS adet
FROM publishing.publications
GROUP BY Status, Channel
ORDER BY Status, Channel;

SELECT TOP 20 Id, Channel, TargetRef, ScheduledAt, Attempts
FROM publishing.publications
WHERE Status IN ('Scheduled', 'Pending')
ORDER BY ScheduledAt;
GO

-- =====================================================================
-- ADIM 8 (İSTEĞE BAĞLI) — Eski delivery_attempts kayıtlarını temizle
-- (sadece log niteliğinde; 30 günden eskileri silmek tabloyu küçültür)
-- =====================================================================
-- DELETE FROM publishing.delivery_attempts
-- WHERE CreatedAt < DATEADD(DAY, -30, SYSDATETIMEOFFSET());
-- GO

-- =====================================================================
-- ADIM 9 (İSTEĞE BAĞLI) — Her şey yolundaysa yedek tabloyu kaldır
-- =====================================================================
-- DROP TABLE publishing.publications_yedek_20260721;
-- GO
