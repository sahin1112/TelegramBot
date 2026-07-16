# 06 — Telegram (Tüm Bölümler)

> Telegram’ın tamamı: yayın, grup yönetimi, admin grubu→taslak, dört reklam türü. MVP’nin ilk yayın kanalı. Sıralı geliştirmede derinleştirilecek. Tam bağlam: `00-PROJE-Ana-Dokuman.md`.

## 1. Yayın (TelegramPublisher)
- Ham Telegram Bot API (HttpClient), TrueNetwork deseni: `sendMessage`/`sendPhoto`. Kimlik `SocialAccount.BotToken`’dan **çağrıda** alınır.
- Metin eşlemesi: `ShortX` veya body kısaltması + hashtag + blog linki; görsel caption ile.
- **BroadcastGuard** (TrueNetwork): günlük limit, paylaşımlar arası min süre, olay bazlı tekilleştirme.
- Hedefler `PublicationTarget` (Group|Channel); bir bot çok hedefe yayar.

## 2. Kanal + tartışma grubu (paylaşımın içinde yorum)
- Kanal bir tartışma grubuna bağlanınca gönderi altında **native yorum thread’i** çıkar. `TargetKind=Channel` + `LinkedDiscussionGroupId`.
- Kurulum tek seferlik/manuel (kanal→grup bağlama, bot her ikisinde admin).
- Link temizleme + aktiflik/rozet **bağlı tartışma grubunda** işler; liderlik kartı kanala.
- Öneri: içerik odaklı hedefler için Kanal+yorum; saf sohbet için düz Group.

## 3. Grup yönetimi (Community)
Bot **privacy kapalı** (`/setprivacy → Disable`) + hedefte **admin**.
- **Link auto-sil (aç/kapa):** `AutoDeleteLinks` + `LinkWhitelist`; gelen mesajda link regex → `deleteMessage`.
- **Aktiflik:** bot mesaj sayar (`UserActivity.MessageCount`); `TrackReactions`+admin ise `message_reaction` güncellemelerinden reaksiyon toplar (ileriye dönük, geçmiş sayılamaz).
- **Rozet:** panelden eşikli `ActivityBadge` (ad, metrik, eşik). Normal kullanıcı → **sanal rozet** (kartlarda/hitapta). **Native admin custom-title yalnız gerçek moderatör/yöneticide** (≤16 karakter); normal kullanıcı sırf rozet için admin yapılmaz.
- **Haftalık liderlik:** `WeeklyLeaderboardJob` → en aktif sıralaması → **SkiaSharp** kart → gruba/kanala. BroadcastGuard ile.
- **Raid/spam koruması (Community fazı):** bekleme, captcha, flood mute, yasaklı kelime, raid modu, uyarı puanı, mute/ban + itiraz.
- **Topluluk görevleri (faz):** reaksiyon/soru cevaplama/öneri/yeni üye yardımı — mesaj sayısı değil; günlük limit.
- **İçerik öneri sistemi (faz):** üye link/haber gönderir → taslak; yayınlanırsa öneren’e rozet/puan.

## 4. Admin grubu → taslak (AdminInbox)
Kapalı grup + admin botu (`AllowedUserIds`). Üye mesajı → `ContentItem(Origin=TelegramAdmin)` → FactPack + AI **taslak** (Title, X-caption, ana makale, IG-caption, Tags) → `ReviewDraft` → panelde düzenle/yeniden üret/onayla → görsel + DistributionPlan. Kategori mesajda `#etiket` veya panelden; çok-kategori.

## 5. Reklam — dört ayrı tür (bkz. 00 §14)
1. **Telegram resmi gelir paylaşımı** (`TelegramOfficialRevenue`): %50, public kanal 1.000+ abone. Biz yayınlamayız; sadece gelir/bakiye/çekim izleriz.
2. **AdsGram kanal reklamı** (`AdsGramChannelIntegration`): AdsGram botu admin olur ve **o** yayınlar; ilk 24s görüntüleme gelir, 24s sonra silinir; public kanal; CPM. `ChannelId, IntegrationStatus, AdsGramBotIsAdmin, ModerationStatus, LastAdPublishedAt/DeletedAt, EstimatedRevenue, FinalRevenue, PayoutStatus`. **Harmanlayıcı içerik kuyruğundan AdsGram çekmez.**
3. **AdsGram bot / Mini App** (`TelegramBotAdIntegration`, `MiniAppAdIntegration`): bot CPC; MiniApp reward/interstitial/task.
4. **Kendi sponsor postumuz** (`DirectCampaign`): sponsor bize öder; **bizim bot** yayınlar; metin/görsel/buton bizde; `/r/{id}` ölçüm; süre sonunda silinebilir; sponsora rapor.

## 6. Kanal / grup / bot / MiniApp ayrımı (reklam)
Kanal: reklam/sponsor/yayın/görüntülenme · Tartışma grubu: topluluk/yorum/moderasyon · Bot: etkileşim/CPC · MiniApp: reward/interstitial/task. Gruba doğrudan sponsor postu = kendi DirectCampaign’imiz (ağ değil).

## 7. Pacing (`AdPacingPolicy`)
`MaxSponsoredPostsPerDay, MinOrganicPostsBetweenAds, MinMinutesBetweenAds, AllowedHours, QuietHours, BlockedCategories, BlockedSensitiveTopics, MaxAdSharePercent, DeleteAfterHours, PinDurationMinutes`. Afet/ölüm/sağlık krizi içeriğinin yanında reklam yok; reklam ≤ toplam %15.

## 8. Bildirim botu
Sistem hataları (token doldu, kaynak kırıldı, yayın başarısız) → **admin Telegram kanalı** anlık bildirim.

---
*Derinleştirilecek:* Bot API metod eşlemesi, getUpdates/webhook döngüsü, SkiaSharp kart şablonları, AdsGram entegrasyon adımları, kurulum kontrol listeleri.
