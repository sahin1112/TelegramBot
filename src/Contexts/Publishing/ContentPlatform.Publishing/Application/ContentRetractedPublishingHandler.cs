using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.Publishing.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Publishing.Application;

/// <summary>
/// İçerik silinince o içeriğe ait HENÜZ GÖNDERİLMEMİŞ (Pending/Scheduled/Failed) yayınları iptal eder
/// (bir daha gönderilmez). Zaten gönderilmiş (Published) paylaşımlara dokunulmaz.
/// </summary>
public sealed class ContentRetractedPublishingHandler(IPublicationRepository repository, IClock clock, ILogger<ContentRetractedPublishingHandler> logger)
    : IIntegrationEventHandler<ContentRetractedIntegrationEvent>
{
    public async Task HandleAsync(ContentRetractedIntegrationEvent e, CancellationToken ct)
    {
        var pubs = await repository.ListAsync(e.ContentItemId, null, 500, ct);
        var cancelled = 0;
        foreach (var p in pubs)
        {
            if (p.Status == PublicationStatus.Published) continue; // gönderilmişe dokunulmaz
            p.Cancel(clock);
            cancelled++;
        }
        if (cancelled > 0)
        {
            await repository.SaveChangesAsync(ct);
            logger.LogInformation("İçerik silindi: {N} bekleyen/planlı yayın iptal edildi ({Id}).", cancelled, e.ContentItemId);
        }
    }
}
