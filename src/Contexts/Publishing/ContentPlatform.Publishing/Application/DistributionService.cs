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
    IClock clock,
    ILogger<DistributionService> logger)
{
    public async Task<bool> PublishOneAsync(Publication pub, CancellationToken ct)
    {
        if (!registry.Supports(pub.Channel)) { pub.MarkFailed("Adaptör yok", clock); return false; }

        var credentials = await credentialProvider.GetAsync(pub.SocialAccountId, ct);
        if (credentials is null) { pub.MarkFailed("Hesap kimliği çözülemedi", clock); return false; }

        var payload = JsonSerializer.Deserialize<PublicationPayload>(pub.PayloadJson)
                      ?? new PublicationPayload(null, "", Array.Empty<string>(), null, null);

        MediaContent? media = null;
        if (!string.IsNullOrWhiteSpace(payload.MediaUrl))
            media = await mediaReader.TryReadAsync(payload.MediaUrl, ct);

        var request = new PublishRequest(
            pub.Channel, payload.Title, payload.Text, payload.Hashtags,
            payload.MediaUrl, payload.Link, pub.TargetRef, media);

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
}
