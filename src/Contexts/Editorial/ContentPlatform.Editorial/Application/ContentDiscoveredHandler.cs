using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Domain;
using ContentPlatform.Ingestion.Contracts;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Editorial.Application;

/// <summary>
/// Ingestion keşif olayını dinler → ContentItem üretir (RSS/sayfa => TASLAK).
/// Kategori/kaynak otomasyon ayarları açıksa içerik "otomatik hazırlanacak" olarak işaretlenir
/// (metin/görsel/video otomatik üretilir; yayına gitmez, AutoPublish açık değilse taslakta bekler).
/// </summary>
public sealed class ContentDiscoveredHandler(
    IContentRepository repository,
    IRiskClassifier riskClassifier,
    IContentAudit audit,
    ICategoryAutomationProvider categoryAutomation,
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
            // KRİTİK: CreatedByRef ve (buradan türeyen) audit.ActorRef kolonları nvarchar(1000).
            // Google News RSS linkleri 400-800 karakter olabiliyordu → "String or binary data would be
            // truncated" ile SaveChanges patlıyor ve İÇERİK HİÇ KAYDEDİLMİYORDU (loglarda 93 hata).
            // Çözüm: tam URL yerine KISA ve ANLAMLI bir aktör referansı üret ("ingestion:host").
            createdByRef: BuildActorRef(e.SourceUrl, e.SourceKind),
            clock: clock);

        // ---- Otomatik üretim niyeti: KAYNAK ayarı kategoriyi EZER (null = kategoriden devral) ----
        var cat = e.CategoryId is { } cid ? await categoryAutomation.GetAsync(cid, ct) : null;
        var content = e.SrcAutoContent ?? cat?.AutoContent ?? false;
        var image = e.SrcAutoImage ?? cat?.AutoImage ?? false;
        var video = e.SrcAutoVideo ?? cat?.AutoVideo ?? false;
        var publish = cat?.AutoPublish ?? false;
        if (content || image || video)
        {
            item.ConfigureAutomation(content, image, video, publish, clock);
            logger.LogInformation("Otomatik hazırlık işaretlendi (metin:{C} görsel:{I} video:{V} yayınla:{P}): {Title}",
                content, image, video, publish, e.RawTitle);
        }

        // Görsel şablon havuzu: kaynak override ?? kategori (boş = varsayılan SkiaCard). Rozet: kategori ayarı.
        var pool1x1 = !string.IsNullOrWhiteSpace(e.SrcCard1x1) ? e.SrcCard1x1! : cat?.Card1x1 ?? "";
        var poolReels = !string.IsNullOrWhiteSpace(e.SrcCardReels) ? e.SrcCardReels! : cat?.CardReels ?? "";
        item.ConfigureCards(pool1x1, poolReels, cat?.AttentionBadges ?? false, clock);

        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.System, item.CreatedByRef, $"Kaynak: {origin}, risk: {risk}");
        await repository.SaveChangesAsync(ct);
        logger.LogInformation("İçerik taslak olarak eklendi: {Title}", e.RawTitle);
    }

    /// <summary>
    /// Kısa, kararlı bir aktör referansı üretir: "ingestion:{host}" (ör. "ingestion:news.google.com").
    /// URL çözülemezse kaynak türüne, o da yoksa "ingestion"a düşer.
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
