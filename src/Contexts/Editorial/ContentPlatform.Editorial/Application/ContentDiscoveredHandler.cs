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
            // KRİTİK: CreatedByRef ve (buradan türeyen) audit.ActorRef kolonları nvarchar(200).
            // Google News RSS linkleri 400-800 karakter olabiliyordu → "String or binary data would be
            // truncated" ile SaveChanges patlıyor ve İÇERİK HİÇ KAYDEDİLMİYORDU (loglarda 93 hata).
            // Çözüm: tam URL yerine KISA ve ANLAMLI bir aktör referansı üret ("ingestion:host").
            // Tam kaynak adresi zaten SourceUrl kolonunda (nvarchar(1000)) saklanıyor — burada tekrarı gereksiz.
            createdByRef: BuildActorRef(e.SourceUrl, e.SourceKind),
            clock: clock);

        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.System, item.CreatedByRef, $"Kaynak: {origin}, risk: {risk}");
        await repository.SaveChangesAsync(ct);
        logger.LogInformation("İçerik onay kuyruğuna eklendi: {Title}", e.RawTitle);
    }

    /// <summary>
    /// Kısa, kararlı bir aktör referansı üretir: "ingestion:{host}" (ör. "ingestion:news.google.com").
    /// URL çözülemezse kaynak türüne, o da yoksa "ingestion"a düşer. Her hâlükârda 200 karakter
    /// sınırının çok altında kalır — kolon taşması bir daha yaşanmaz.
    /// </summary>
    private static string BuildActorRef(string? sourceUrl, string? sourceKind)
    {
        string tail;
        if (!string.IsNullOrWhiteSpace(sourceUrl) &&
            Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
            tail = uri.Host;                                   // ör. news.google.com
        else
            tail = string.IsNullOrWhiteSpace(sourceKind) ? "system" : sourceKind!;

        var reference = $"ingestion:{tail}";
        return reference.Length > 190 ? reference[..190] : reference; // ekstra güvenlik payı
    }
}
