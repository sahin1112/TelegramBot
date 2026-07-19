using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Publishing.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Application;

/// <summary>
/// Tek bir Publication'ı yayınlar: kimlik çöz → görseli oku (multipart) → adaptörle gönder → durum güncelle.
/// Başarılıysa ContentPublished olayı yayınlar. Handler ve retry job aynı yolu kullanır.
/// </summary>
public sealed class DistributionService(
    IAccountCredentialProvider credentialProvider,
    IChannelPublisherRegistry registry,
    IMediaReader mediaReader,
    IIntegrationEventPublisher bus,
    IKillSwitch killSwitch,
    ISettingsProvider settings,
    IClock clock,
    ILogger<DistributionService> logger)
{
    /// <summary>Platform başına varsayılan medya önceliği (panelden media.pref.{kanal} ile değişir).
    /// "video" → video varsa video, yoksa görsel · "image" → görsel varsa görsel, yoksa video.</summary>
    private static string DefaultPref(Channel c) => c switch
    {
        Channel.Instagram => "video", // Reels önceliği
        Channel.Youtube => "video",   // Shorts
        Channel.TikTok => "video",
        _ => "image"                  // Telegram / X / Threads varsayılan görsel
    };

    public async Task<bool> PublishOneAsync(Publication pub, CancellationToken ct)
    {
        // Acil durdurma: fren çekiliyse gönderme; Failed sayma, beklet (fren kalkınca dispatch dener).
        if (await killSwitch.IsPublishingStoppedAsync(ToPlatform(pub.Channel), pub.CategoryId, pub.SocialAccountId, ct))
        {
            pub.HoldForKillSwitch(clock);
            logger.LogWarning("Yayın durduruldu (kill-switch): kanal={Channel} hedef={Target}", pub.Channel, pub.TargetRef);
            return false;
        }

        if (!registry.Supports(pub.Channel)) { pub.MarkFailed("Adaptör yok", clock); return false; }

        var credentials = await credentialProvider.GetAsync(pub.SocialAccountId, ct);
        if (credentials is null) { pub.MarkFailed("Hesap kimliği çözülemedi", clock); return false; }

        var payload = JsonSerializer.Deserialize<PublicationPayload>(pub.PayloadJson)
                      ?? new PublicationPayload(null, "", Array.Empty<string>(), null, null);

        MediaContent? media = null;
        if (!string.IsNullOrWhiteSpace(payload.MediaUrl))
            media = await mediaReader.TryReadAsync(payload.MediaUrl, ct);

        // ---- Platform başına medya önceliği: öncelikli tür varsa YALNIZ o gönderilir; yoksa öteki. ----
        // Panel ayarı: media.pref.telegram / media.pref.instagram ... ("image" | "video"). Boşsa varsayılan.
        var pref = (await settings.GetAsync($"media.pref.{pub.Channel.ToString().ToLowerInvariant()}", ct))?.Trim().ToLowerInvariant();
        if (pref is not ("image" or "video")) pref = DefaultPref(pub.Channel);
        var mediaUrl = payload.MediaUrl;
        var videoUrl = payload.VideoUrl;
        var hasImage = media is not null || !string.IsNullOrWhiteSpace(mediaUrl);
        var hasVideo = !string.IsNullOrWhiteSpace(videoUrl);
        if (pref == "video" && hasVideo)
        {
            // Video öncelikli → görseli düşür. İSTİSNA: Instagram'da görsel TUTULUR — akış gönderisi
            // yine video (Reels) olur ama HİKAYE görselden atılır (anında yayınlanır, video işleme
            // beklemesi yok; kartta başlık+alan adı basılı). Adaptör: akış=video öncelikli, hikaye=görsel öncelikli.
            if (pub.Channel != Channel.Instagram) { media = null; mediaUrl = null; }
        }
        else if (pref == "image" && hasImage) { videoUrl = null; }               // görsel öncelikli → videoyu düşür
        // (öncelikli tür YOKSA dokunma → adaptör mevcut olanı gönderir)

        // YouTube/TikTok YALNIZ video paylaşır: içerikte video yoksa Failed kirliliği ve boş retry
        // üretme — yayını sessizce İPTAL et (Skipped; panelde görünür, sayaç artmaz).
        if (pub.Channel is Channel.Youtube or Channel.TikTok && !hasVideo)
        {
            pub.Cancel(clock);
            logger.LogInformation("Video yok → {Channel} yayını atlandı: {Target}", pub.Channel, pub.TargetRef);
            return false;
        }

        // Video da görsel gibi önce DOSYA olarak okunur (multipart en sağlamı); yerelde yoksa
        // adaptör public URL'e düşer (Telegram URL'den kendisi indirir).
        MediaContent? videoMedia = null;
        if (!string.IsNullOrWhiteSpace(videoUrl))
            videoMedia = await mediaReader.TryReadAsync(videoUrl!, ct);

        // Instagram'a özel uzun açıklama (payload.IgCaption) varsa ShortX yerine o gönderilir.
        var text = pub.Channel == Channel.Instagram && !string.IsNullOrWhiteSpace(payload.IgCaption)
            ? payload.IgCaption! : payload.Text;

        var request = new PublishRequest(
            pub.Channel, payload.Title, text, payload.Hashtags,
            mediaUrl, payload.Link, pub.TargetRef, media, payload.ButtonUrl, payload.ButtonText, videoUrl, videoMedia);

        var result = await registry.Resolve(pub.Channel).PublishAsync(request, credentials, ct);

        if (result.Published)
        {
            pub.MarkPublished(result.ExternalId, clock);
            await bus.PublishAsync(new ContentPublishedIntegrationEvent(Guid.NewGuid(), clock.UtcNow, pub.ContentItemId), ct);
            logger.LogInformation("Yayınlandı: hedef={Target} extId={ExtId}", pub.TargetRef, result.ExternalId);
            return true;
        }

        pub.MarkFailed(result.Error?.Message, clock);
        logger.LogWarning("Yayın hatası: hedef={Target} deneme={Attempts} hata={Error}", pub.TargetRef, pub.Attempts, result.Error?.Message);
        return false;
    }

    private static Platform ToPlatform(Channel c) => c switch
    {
        Channel.Telegram => Platform.Telegram,
        Channel.X => Platform.X,
        Channel.Instagram => Platform.Instagram,
        Channel.Threads => Platform.Threads,
        Channel.Youtube => Platform.Youtube,
        Channel.TikTok => Platform.TikTok,
        _ => Platform.Telegram
    };
}
