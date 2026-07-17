using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Domain;
using ContentPlatform.Ingestion.Contracts;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Editorial.Application;

/// <summary>
/// Ingestion keşif olayını dinler → ContentItem üretir (RSS/sayfa => PendingReview: AI yok, önce onay).
/// AI (metin+görsel) yalnız onaydan sonra çalışır — maliyet kontrolü.
/// </summary>
public sealed class ContentDiscoveredHandler(
    IContentRepository repository,
    IRiskClassifier riskClassifier,
    IContentAudit audit,
    IClock clock,
    ILogger<ContentDiscoveredHandler> logger)
    : IIntegrationEventHandler<ContentDiscoveredIntegrationEvent>
{
    public async Task HandleAsync(ContentDiscoveredIntegrationEvent e, CancellationToken ct)
    {
        if (await repository.ExistsByHashAsync(e.SourceHash, ct)) return; // ikinci güvenlik

        var origin = Enum.TryParse<ContentOrigin>(e.SourceKind, out var o) ? o : ContentOrigin.Rss;
        var risk = riskClassifier.Classify(e.RawTitle, e.RawInput);

        var item = new ContentItem(
            origin: origin,
            useAi: true,
            imageSource: ImageSource.SkiaCard,   // varsayılan; onay ekranında değişebilir
            riskLevel: risk,                     // metinden sınıflandırıldı (yüksek risk oto-onaylanamaz)
            categoryId: e.CategoryId,
            testMode: false,
            sourceHash: e.SourceHash,
            sourceUrl: e.SourceUrl,
            rawTitle: e.RawTitle,
            rawInput: e.RawInput,
            createdByType: ActorType.System,
            createdByRef: $"ingestion:{e.SourceUrl ?? e.SourceKind}",
            clock: clock);

        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.System, item.CreatedByRef, $"Kaynak: {origin}, risk: {risk}");
        await repository.SaveChangesAsync(ct);
        logger.LogInformation("İçerik onay kuyruğuna eklendi: {Title}", e.RawTitle);
    }
}
