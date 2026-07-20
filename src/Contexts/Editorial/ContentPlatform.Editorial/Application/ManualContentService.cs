using ContentPlatform.Editorial.Domain;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Editorial.Application;

/// <summary>Panelden manuel içerik ekleme — AI'lı (taslak ürettir) ve AI'sız (bitmiş metin).</summary>
public sealed class ManualContentService(IContentRepository repository, IRiskClassifier riskClassifier, IContentAudit audit, IClock clock)
{
    /// <summary>AI'lı: metni yapıştır → otomatik onaylı → PipelineDrainJob AI ile üretir.</summary>
    public async Task<Guid> AddWithAiAsync(AddManualAiRequest req, CancellationToken ct)
    {
        var item = new ContentItem(
            ContentOrigin.Manual, useAi: true, req.ImageSource, riskClassifier.Classify(req.Title, req.RawInput),
            req.CategoryId, req.TestMode, NewHash(), sourceUrl: null,
            rawTitle: req.Title, rawInput: req.RawInput,
            ActorType.AdminUser, createdByRef: "admin", clock);
        item.Schedule(req.ScheduledAt, clock);
        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.AdminUser, "admin", "Manuel (AI'lı)");
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>
    /// AI'lı ama ÖNİZLEME/gözden geçirme için: içeriği PendingReview olarak oluşturur (otomatik yayınlanmaz).
    /// Endpoint bunu oluşturup hemen GenerateDraftAsync ile doldurur → kullanıcı önizler/düzenler/onaylar.
    /// Aynı motor Telegram admin kanalı (D1) için de kullanılacak.
    /// </summary>
    public async Task<Guid> AddForReviewAsync(AddManualAiRequest req, CancellationToken ct)
    {
        var item = new ContentItem(
            ContentOrigin.Manual, useAi: true, req.ImageSource, riskClassifier.Classify(req.Title, req.RawInput),
            req.CategoryId, req.TestMode, NewHash(), sourceUrl: null,
            rawTitle: req.Title, rawInput: req.RawInput,
            ActorType.AdminUser, createdByRef: "admin", clock);
        item.ReturnToReview(clock);          // otomatik onaylı olmasın → önce üret + gözden geçir
        item.Schedule(req.ScheduledAt, clock);
        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.AdminUser, "admin", "Manuel (AI, önizleme)");
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>AI'sız: bitmiş metni doğrudan → revizyon oluşturulur → görsel + yayına girer (AI yok).</summary>
    public async Task<Guid> AddWithoutAiAsync(AddManualNoAiRequest req, CancellationToken ct)
    {
        var item = new ContentItem(
            ContentOrigin.ManualNoAi, useAi: false, req.ImageSource, riskClassifier.Classify(req.Title, req.BodyHtml),
            req.CategoryId, req.TestMode, NewHash(), sourceUrl: null,
            rawTitle: req.Title, rawInput: req.BodyHtml,
            ActorType.AdminUser, createdByRef: "admin", clock);

        item.AddRevision(new ContentRevision(
            item.Id, 1, req.Title, req.ShortX, req.BodyHtml, req.InstagramCaption,
            req.Tags, req.PrimaryKeyword, req.ImageAltText, createdBy: "admin", clock));
        item.Schedule(req.ScheduledAt, clock);

        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.AdminUser, "admin", "Manuel (AI'sız, bitmiş metin)");
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>
    /// Telegram admin grubundan LİNK ile içerik: /kategori https://... → içerik TASLAK oluşturulur
    /// (Origin=TelegramAdmin), SourceUrl'den TAM makale metni çekilip AI üretimi yapılır (çağıran
    /// GenerateDraftAsync + SubmitForReview'i tamamlar) → ONAY kuyruğuna düşer; otomatik yayınlanmaz.
    /// </summary>
    public async Task<Guid> AddFromLinkForReviewAsync(string url, Guid? categoryId, string createdByRef, CancellationToken ct)
    {
        var item = new ContentItem(
            ContentOrigin.TelegramAdmin, useAi: true, ImageSource.SkiaCard,
            riskClassifier.Classify(null, url), categoryId, testMode: false,
            NewHash(), sourceUrl: url, rawTitle: null, rawInput: null,
            ActorType.TelegramMember, createdByRef, clock);
        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.TelegramMember, createdByRef, "Telegram admin komutu (link)");
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    private static string NewHash() => $"manual:{Guid.NewGuid():N}";
}
