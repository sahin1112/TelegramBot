# 09 — Threads Entegrasyonu

> Threads ile ilgili yapılacakların tamamı. Sıralı geliştirmede derinleştirilecek. Tam bağlam: `00-PROJE-Ana-Dokuman.md`.

## 1. Ön koşullar
- **Resmi Threads API** kullanılır (Meta). API görece **kısıtlı**; desteklenen içerik türleri ve limitler sınırlı.
- Meta App + Threads izinleri; hesap yetkilendirmesi.

## 2. Kimlik (şifreli DB, panelden)
`ThreadsUserId, AccessToken` (+ `TokenExpiresAt`).
- Token yenileme çekirdek: `TokenRefreshJob` süresi yaklaşanı yeniler; yenilenemezse hesap `Error` + admin bildirimi (bkz. 03 §4).

## 3. Yayın akışı
- Threads yayın akışı (container/publish benzeri) resmi API adımlarına göre; metin + görsel + link.
- Metin: `ShortX` veya `InstagramCaption` uyarlaması + hashtag; ton: “sohbet dili” (AccountVoiceProfile — faz).

## 4. Limitler
- Yayın kotası/rate-limit farkındalığı; retry + idempotency (`ExternalId`).

## 5. Faz
Threads **Faz 2** (X’ten sonra, Instagram’dan önce). Adaptör `IChannelPublisher` deseninde; kimlik çağrıda alınır.

---
*Derinleştirilecek:* resmi API endpoint/scope/limit tablosu, yayın akışı kod iskeleti, hata kodları, token yenileme ayrıntısı.
