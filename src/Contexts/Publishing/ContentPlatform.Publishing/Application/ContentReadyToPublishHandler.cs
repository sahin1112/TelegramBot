using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Publishing.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Application;

/// <summary>
/// Yayına-hazır içeriği hedeflere dağıtır. Hedefler role göre çözülür (Editorial/Test; AdminInbox HARİÇ).
/// Her hedef için bir Publication (idempotent) oluşturulur ve DistributionService ile yayınlanır.
/// </summary>
public sealed class ContentReadyToPublishHandler(
    IPublicationTargetResolver targetResolver,
    IPublicationRepository publications,
    DistributionService distribution,
    IChannelPublisherRegistry registry,
    ISettingsProvider settings,
    ISchedulePlanner planner,
    IClock clock,
    ILogger<ContentReadyToPublishHandler> logger)
    : IIntegrationEventHandler<ContentReadyToPublishIntegrationEvent>
{
    /// <summary>Sosyal yayın kanalları (Blog hariç). Yalnız ADAPTÖRÜ KAYITLI olanlara dağıtılır —
    /// adaptörsüz kanala Publication açılmaz (Failed kirliliği/istikrarsızlık olmaz). Yeni adaptör
    /// eklendiğinde (X/IG/Threads/YouTube/TikTok) o kanal OTOMATİK devreye girer.</summary>
    private static readonly Channel[] SocialChannels =
        { Channel.Telegram, Channel.X, Channel.Instagram, Channel.Threads, Channel.Youtube, Channel.TikTok };

    public async Task HandleAsync(ContentReadyToPublishIntegrationEvent e, CancellationToken ct)
    {
        // Yayın zamanı: TEST içerik daima HEMEN (test kanalı). Aksi halde elle verilen zaman,
        // o da yoksa kategori kadans politikasından bir sonraki slot. null → hemen.
        DateTimeOffset? scheduledAt = null;
        if (!e.TestMode)
            scheduledAt = e.ScheduledAt ?? await planner.NextSlotAsync(e.CategoryId, ct);

        var (buttonUrl, buttonText) = await BuildDetailButtonAsync(e, ct);
        // Etiketler ham metin degil HASHTAG olarak gider: "kripto, bitcoin" -> #Kripto #Bitcoin
        var payloadJson = JsonSerializer.Serialize(new PublicationPayload(e.Title, e.ShortX, ToHashtags(e.Tags), e.MediaUrl, e.Link, buttonUrl, buttonText, e.VideoUrl, e.InstagramCaption));

        var totalTargets = 0;
        foreach (var channel in SocialChannels)
        {
            if (!registry.Supports(channel)) continue; // adaptörü yok → bu kanala hiç Publication açma

            // KANAL İZOLASYONU: bir kanalın hedef çözümü/gönderimi patlasa bile KALAN kanallar işlenir.
            // (Eskiden tek istisna tüm döngüyü öldürüyordu → "4 platformdan 2'sine gitti, 2'sine hiç
            // Publication açılmadı" tutarsızlığının ana kaynağıydı.)
            IReadOnlyList<ResolvedTarget> targets;
            try { targets = await targetResolver.ResolveAsync(e.CategoryId, e.TestMode, channel, ct); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hedefler çözülemedi (kanal={Channel}) — öteki kanallar etkilenmez.", channel);
                continue;
            }
            if (targets.Count == 0) continue;
            totalTargets += targets.Count;

            foreach (var target in targets)
            {
                Publication? pub = null;
                try
                {
                    var existing = await publications.FindAsync(e.ContentItemId, channel, target.ExternalTargetId, ct);
                    if (existing is { Status: PublicationStatus.Published }) continue; // idempotent

                    pub = existing;
                    if (pub is null)
                    {
                        pub = new Publication(e.ContentItemId, channel, target.SocialAccountId, target.ExternalTargetId, payloadJson,
                            e.CategoryId, scheduledAt, clock);
                        await publications.AddAsync(pub, ct);
                    }
                    else
                    {
                        // İçerik yeniden yayına gönderildi (düzenleme / elle "Yayınla"): anlık kopyayı tazele
                        // ve hedefin GÜNCEL hesabına bağla (hesap silinip yeniden bağlandıysa Id değişmiştir).
                        pub.Rebind(target.SocialAccountId, clock);
                        pub.RefreshPayload(payloadJson, clock);
                        // Elle yeni zaman verildiyse ona çek; Failed ise plana/hemene alıp YENİDEN dene.
                        if (e.ScheduledAt is not null || pub.Status == PublicationStatus.Failed)
                            pub.Reschedule(e.ScheduledAt ?? scheduledAt, clock);
                    }

                    // Kayıt gönderimden ÖNCE kalıcılaşır: süreç tam bu anda kesilse bile yayın KAYBOLMAZ —
                    // takılı kalan Pending kayıtlarını OutboxDispatchJob kurtarıp yeniden planlar.
                    await publications.SaveChangesAsync(ct);

                    // Gelecek bir zamana planlıysa şimdi gönderme; ScheduledDispatchJob zamanı gelince gönderir.
                    if (pub.Status == PublicationStatus.Scheduled) continue;

                    await distribution.PublishOneAsync(pub, ct);
                    await publications.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    // HEDEF İZOLASYONU: tek hedef patladı → yalnız BU hedefi 1 dk sonraya planla,
                    // kalan hedefler/kanallar devam etsin. Kayıt CancellationToken.None ile yazılır —
                    // istek iptali durumu bile kaydı engelleyemez.
                    logger.LogError(ex, "Yayın hedefi işlenemedi (kanal={Channel} hedef={Target}); 1 dk sonraya planlandı.", channel, target.ExternalTargetId);
                    try
                    {
                        if (pub is not null)
                        {
                            pub.Reschedule(clock.UtcNow.AddMinutes(1), clock);
                            await publications.SaveChangesAsync(CancellationToken.None);
                        }
                    }
                    catch (Exception ex2)
                    {
                        logger.LogError(ex2, "Yayın kurtarma kaydı da başarısız (kanal={Channel} hedef={Target}).", channel, target.ExternalTargetId);
                    }
                }
            }
        }

        if (totalTargets == 0)
        {
            logger.LogInformation("Yayın hedefi yok (kategori={Cat}, test={Test}).", e.CategoryId, e.TestMode);
            return;
        }
        if (scheduledAt is { } at)
            logger.LogInformation("İçerik {Id} {Time} için planlandı ({Count} hedef).", e.ContentItemId, at, totalTargets);
    }
    /// <summary>
    /// Gonderi altindaki "Haber ayrintisi" butonunu kurar. AdGate ise Mini App derin linki (Adsgram reklami),
    /// degilse dogrudan makale linki. Link yoksa (test icerik) buton yok.
    /// </summary>
    private async Task<(string? Url, string? Text)> BuildDetailButtonAsync(ContentReadyToPublishIntegrationEvent e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Link)) return (null, null);
        const string text = "Haber ayrıntısı için tıkla";

        var url = e.Link;
        // Mini App tanimliysa haber HER ZAMAN mini app icinde acilir (Telegram'dan cikmadan).
        // Reklam yalniz 'Reklam izlet' isaretliyse gosterilir: startapp sonuna "--ad" eklenir;
        // /ad-gate sayfasi bu isarete gore Adsgram'i acar ya da dogrudan habere gecer.
        var miniapp = await settings.GetAsync("telegram.miniapp_url", ct);
        if (!string.IsNullOrWhiteSpace(miniapp))
        {
            // Panelde 't.me/Bot/app' gibi ŞEMASIZ girilirse geçersiz sayılıp linkin siteye düşmesini önle:
            // http(s) yoksa başına https:// ekle.
            miniapp = miniapp.Trim();
            if (!miniapp.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !miniapp.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                miniapp = "https://" + miniapp;
            // Telegram startapp EN FAZLA 64 KARAKTER; uzun haber slug'ları kırpılıp mini app ANA SAYFAYA
            // düşüyordu. Bu yüzden startapp'a KISA kod (ContentItemId, 32 hex) koyuyoruz; site /r/{kod}
            // ile bunu gerçek slug'a çevirip makaleye yönlendirir. (kod + "--ad" en fazla 36 karakter.)
            var code = e.ContentItemId.ToString("N");
            var sep = miniapp.Contains('?') ? "&" : "?";
            url = $"{miniapp}{sep}startapp={code}{(e.AdGate ? "--ad" : "")}";
        }

        // Telegram inline buton URL'i herkese açık geçerli bir http(s) adresi olmalı.
        // localhost/relative/geçersiz ise butonu ekleme (gönderi yine de gitsin, tüm yayın patlamasın).
        if (!IsPublicHttpUrl(url))
        {
            logger.LogWarning("Buton URL'i geçersiz/herkese açık değil, buton eklenmedi: {Url}", url);
            return (null, null);
        }
        return (url, text);
    }

    /// <summary>
    /// Etiket listesini sosyal medya hashtag'lerine çevirir: tek etikete sıkışmış virgüllüler ayrılır,
    /// noktalama temizlenir, çok kelimeliler BüyükHarfle bitişik yazılır (#YapayZeka).
    /// '_' ile başlayan iç işaretler (örn. _ads) HİÇBİR ZAMAN yayınlanmaz. En fazla 8 hashtag.
    /// </summary>
    internal static IReadOnlyList<string> ToHashtags(IEnumerable<string>? tags)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var list = new List<string>();
        foreach (var raw in tags ?? Enumerable.Empty<string>())
        {
            foreach (var piece in (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (piece.StartsWith('_')) continue; // iç işaret — dışarı sızmaz
                var words = Regex.Matches(piece, @"[\p{L}\p{Nd}]+").Select(m => m.Value).ToList();
                if (words.Count == 0) continue;
                var joined = string.Concat(words.Select(w =>
                    char.ToUpper(w[0], tr) + (w.Length > 1 ? w[1..].ToLower(tr) : "")));
                var tag = "#" + joined;
                if (!list.Contains(tag, StringComparer.OrdinalIgnoreCase)) list.Add(tag);
                if (list.Count >= 8) return list;
            }
        }
        return list;
    }

    private static bool IsPublicHttpUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (string.Equals(u.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (u.Host is "127.0.0.1" or "::1") return false;
        return true;
    }
}

