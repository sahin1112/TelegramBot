using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Domain;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Editorial.Application;

/// <summary>Panelden manuel içerik ekleme — AI'lı (taslak ürettir) ve AI'sız (bitmiş metin).</summary>
public sealed class ManualContentService(
    IContentRepository repository,
    IRiskClassifier riskClassifier,
    IContentAudit audit,
    ICategoryAutomationProvider categoryAutomation,
    IClock clock)
{
    /// <summary>Kategorinin görsel şablon havuzunu (ve rozet ayarını) içeriğe uygular.</summary>
    private async Task ApplyCardsAsync(ContentItem item, CancellationToken ct)
    {
        if (item.CategoryId is not { } cid) return;
        var cat = await categoryAutomation.GetAsync(cid, ct);
        if (cat is null) return;
        item.ConfigureCards(cat.Card1x1, cat.CardReels, cat.AttentionBadges, clock);
    }

    /// <summary>AI'lı: metni yapıştır → otomatik onaylı → PipelineDrainJob AI ile üretir.</summary>
    public async Task<Guid> AddWithAiAsync(AddManualAiRequest req, CancellationToken ct)
    {
        var item = new ContentItem(
            ContentOrigin.Manual, useAi: true, req.ImageSource, riskClassifier.Classify(req.Title, req.RawInput),
            req.CategoryId, req.TestMode, NewHash(), sourceUrl: null,
            rawTitle: req.Title, rawInput: req.RawInput,
            ActorType.AdminUser, createdByRef: "admin", clock);
        item.Schedule(req.ScheduledAt, clock);
        await ApplyCardsAsync(item, ct);
        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.AdminUser, "admin", "Manuel (AI'lı)");
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>AI'lı ama ÖNİZLEME/gözden geçirme için: PendingReview olarak oluşturur.</summary>
    public async Task<Guid> AddForReviewAsync(AddManualAiRequest req, CancellationToken ct)
    {
        var item = new ContentItem(
            ContentOrigin.Manual, useAi: true, req.ImageSource, riskClassifier.Classify(req.Title, req.RawInput),
            req.CategoryId, req.TestMode, NewHash(), sourceUrl: null,
            rawTitle: req.Title, rawInput: req.RawInput,
            ActorType.AdminUser, createdByRef: "admin", clock);
        item.ReturnToReview(clock);
        item.Schedule(req.ScheduledAt, clock);
        await ApplyCardsAsync(item, ct);
        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.AdminUser, "admin", "Manuel (AI, önizleme)");
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>AI'sız: bitmiş metni doğrudan → revizyon oluşturulur → görsel + yayına girer.</summary>
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
        await ApplyCardsAsync(item, ct);

        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.AdminUser, "admin", "Manuel (AI'sız, bitmiş metin)");
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>Telegram admin grubundan LİNK ile içerik → TASLAK (Origin=TelegramAdmin).</summary>
    public async Task<Guid> AddFromLinkForReviewAsync(string url, Guid? categoryId, string createdByRef, CancellationToken ct)
    {
        var item = new ContentItem(
            ContentOrigin.TelegramAdmin, useAi: true, ImageSource.SkiaCard,
            riskClassifier.Classify(null, url), categoryId, testMode: false,
            NewHash(), sourceUrl: url, rawTitle: null, rawInput: null,
            ActorType.TelegramMember, createdByRef, clock);
        await ApplyCardsAsync(item, ct);
        await repository.AddAsync(item, ct);
        audit.Log(item.Id, AuditEvent.Created, ActorType.TelegramMember, createdByRef, "Telegram admin komutu (link)");
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    private static string NewHash() => $"manual:{Guid.NewGuid():N}";
}
