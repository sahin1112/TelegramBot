using System.Text.Json;
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
    ISchedulePlanner planner,
    IClock clock,
    ILogger<ContentReadyToPublishHandler> logger)
    : IIntegrationEventHandler<ContentReadyToPublishIntegrationEvent>
{
    public async Task HandleAsync(ContentReadyToPublishIntegrationEvent e, CancellationToken ct)
    {
        const Channel channel = Channel.Telegram; // MVP; diğer kanallar aynı desenle

        var targets = await targetResolver.ResolveAsync(e.CategoryId, e.TestMode, channel, ct);
        if (targets.Count == 0)
        {
            logger.LogInformation("Yayın hedefi yok (kategori={Cat}, test={Test}).", e.CategoryId, e.TestMode);
            return;
        }

        // Yayın zamanı: TEST içerik daima HEMEN (test kanalı). Aksi halde elle verilen zaman,
        // o da yoksa kategori kadans politikasından bir sonraki slot. null → hemen.
        DateTimeOffset? scheduledAt = null;
        if (!e.TestMode)
            scheduledAt = e.ScheduledAt ?? await planner.NextSlotAsync(e.CategoryId, ct);

        var payloadJson = JsonSerializer.Serialize(new PublicationPayload(e.Title, e.ShortX, e.Tags, e.MediaUrl, e.Link));

        foreach (var target in targets)
        {
            var existing = await publications.FindAsync(e.ContentItemId, channel, target.ExternalTargetId, ct);
            if (existing is { Status: PublicationStatus.Published }) continue; // idempotent

            var pub = existing;
            if (pub is null)
            {
                pub = new Publication(e.ContentItemId, channel, target.SocialAccountId, target.ExternalTargetId, payloadJson,
                    e.CategoryId, scheduledAt, clock);
                await publications.AddAsync(pub, ct);
            }

            // Gelecek bir zamana planlıysa şimdi gönderme; ScheduledDispatchJob zamanı gelince gönderir.
            if (pub.Status == PublicationStatus.Scheduled)
            {
                await publications.SaveChangesAsync(ct);
                continue;
            }

            await distribution.PublishOneAsync(pub, ct);
            await publications.SaveChangesAsync(ct);
        }

        if (scheduledAt is { } at)
            logger.LogInformation("İçerik {Id} {Time} için planlandı ({Count} hedef).", e.ContentItemId, at, targets.Count);
    }
}
