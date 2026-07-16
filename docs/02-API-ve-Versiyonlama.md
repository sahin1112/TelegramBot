# 02 — API Tasarımı ve Versiyonlama

> Hem kendi API’lerimiz (public blog + admin) hem de bağımlı olduğumuz dış API’ler ve sürüm yönetimi. Ayrıntı derinleştirmesi sıralı geliştirmede. Tam bağlam: `00-PROJE-Ana-Dokuman.md`.

## 1. Kendi API’lerimiz
İki yüzey:
- **Public API** (blog/okuyucu): SSR blog + gerekli JSON uçları (yorum gönderme, arama, `/r/{id}` tıklama yönlendirme, sitemap/feed). Anonim, rate-limitli.
- **Admin API** (`/api/admin/*`): panel; kimlik doğrulamalı (tek/çoklu kullanıcı, RBAC-lite), IP kısıtlı.

Stil: ASP.NET Core **Minimal API**, modül başına `*Endpoints.cs` (TrueNetwork deseni). DTO’lar Application katmanında; Domain sızmaz.

## 2. Sürümleme stratejisi
- **URL tabanlı:** `/api/v1/...` (kırıcı değişiklikte `/v2`). Basit, keşfedilebilir.
- **Sözleşme kararlılığı:** public uçlar geriye dönük uyumlu; kırıcı değişiklik yeni sürüm.
- **Deprecation:** eski sürüm bir süre paralel; `Deprecation`/`Sunset` header + changelog.
- **İç Integration Event’ler** de sürümlenir (event şeması değişince yeni tip/alan; tüketiciler kırılmaz).

## 3. Hata ve yanıt standardı
- **Problem Details (RFC 7807)** hata gövdesi.
- Tutarlı zarf: `{ data, error, meta }` (veya salt Problem Details).
- Idempotency: kritik yazma uçlarında `Idempotency-Key` başlığı.

## 4. Dış API istemcileri (sürüm sabitleme)
Bağımlı olduğumuz dış servisler; **her biri için sürüm sabitlenir**, ayardan yönetilir (kodda gömülü değil). Kimlik bilgileri şifreli DB’de (bkz. 03).

| Servis | Kullanım | Sürüm/uç notu |
|---|---|---|
| **OpenAI** | metin + görsel üretimi | model + endpoint `ModelCatalog`’ta konfigüre; Batch API opsiyonu (zamana duyarsız üretim %50 ucuz) |
| **Telegram Bot API** | yayın, grup yönetimi, admin bot | `getUpdates` long-poll veya webhook; sürüm Telegram’ın kendi sürümü |
| **X (Twitter) API** | tweet + medya | v1.1 medya upload + v2 tweet; OAuth 1.0a (bkz. 07) |
| **Instagram Graph API** | yayın | Graph API sürümü (ör. `vXX.0`) sabitlenir; uzun-ömürlü token (bkz. 08) |
| **Threads API** | yayın | resmi Threads API sürümü (bkz. 09) |
| **AdsGram** | Telegram reklam | kanal/bot entegrasyonları; API-tabanlı (bkz. 06) |
| **AdSense/CMP** | blog reklam | script + politika (bkz. 05) |

## 5. Dayanıklılık desenleri (dış çağrılar)
- **Rate-limit farkındalığı** (Redis token-bucket, platform başına).
- **Retry + üstel geri çekilme + circuit breaker** (Polly).
- **Timeout + iptal (CancellationToken)** her çağrıda.
- **Idempotency:** dış yayında `ExternalId` kontrolü; “cevap gelmedi” → körlemesine tekrar yok.
- **Sağlayıcı soyutlama + fallback** (özellikle AI): tek sağlayıcıya kilitlenme yok.

## 6. Webhook vs polling
- Telegram: MVP’de long-poll (webhook’suz, TrueNetwork deseni); ölçekte webhook.
- Gelen olaylar (Telegram mesajı → link temizleme/aktiflik/admin-grubu taslak) olay-güdümlü işlenir.

---
*Derinleştirilecek:* uç uç endpoint listesi, DTO şemaları, örnek istek/yanıt, her dış API için tam sürüm/scope/limit tablosu, Polly politika örnekleri.
