# 08 — Instagram Entegrasyonu

> Instagram ile ilgili yapılacakların tamamı. En çok sürtünmeli platform; TrueNetwork adaptörü temel. Sıralı geliştirmede derinleştirilecek. Tam bağlam: `00-PROJE-Ana-Dokuman.md`.

## 1. Ön koşullar (önemli)
- **Instagram Graph API** kullanılır — kişisel hesaba kolay post atılmaz.
- **Business veya Creator hesabı** + bir **Facebook Sayfası** ile bağlantı gerekir.
- Meta App + gerekli izinler (`instagram_basic`, `instagram_content_publish`, sayfa izinleri).

## 2. Kimlik (şifreli DB, panelden)
`IgUserId, PageId, LongLivedToken` (+ `TokenExpiresAt`).
- **Uzun-ömürlü token ~60 gün** geçerli; **otomatik yenilenmezse yayın sessizce durur.**
- **Hesap ekleme ekranında** uzun-ömürlü token istenir, son kullanım gösterilir.
- **`TokenRefreshJob` (günlük):** süresi yaklaşanı yeniler (bkz. 03 §4). Yenilenemezse hesap `Error` + admin bildirimi.

## 3. Yayın akışı (iki adım)
1. **Media container oluştur:** görsel URL + caption ile container (`/{ig-user-id}/media`).
2. **Yayınla:** container’ı publish et (`/{ig-user-id}/media_publish`).
- Görsel **erişilebilir bir URL**’de olmalı (object storage/CDN — bkz. 01).

## 4. İçerik eşlemesi
- **`InstagramCaption` ≤ 2200 karakter** + hashtag. Makale (LongBody) IG’ye uzun/uygunsuzsa **ayrı IG caption** üretilir (bkz. 00 §8).
- Görsel oranı: kare (1080×1080) veya dikey (1080×1350); tek render → IG boyutu.
- Link IG gönderisinde tıklanabilir değil → “profildeki link”/bio stratejisi.

## 5. Limitler
- Günlük yayın kotası (Graph API sınırları); rate-limit farkındalığı.
- Idempotency: yayın sonucu `ExternalId`; retry’da çift-post önleme.

## 6. Faz
IG **Faz 2/3** (X → Threads → Instagram zorluk sırası). Token yenileme çekirdek (ertelenmez).

---
*Derinleştirilecek:* Meta App kurulum adımları, izin/scope tablosu, container→publish kod iskeleti, hata kodları, carousel/reels ileride.
