using ContentPlatform.Editorial.Domain;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Editorial.Application;

/// <summary>Panelden manuel içerik ekleme — AI'lı (taslak ürettir) ve AI'sız (bitmiş metin).</summary>
public sealed class ManualContentService(IContentRepository repository, IClock clock)
{
    /// <summary>AI'lı: metni yapıştır → otomatik onaylı → PipelineDrainJob AI ile üretir.</summary>
    public async Task<Guid> AddWithAiAsync(AddManualAiRequest req, CancellationToken ct)
    {
        var item = new ContentItem(
            ContentOrigin.Manual, useAi: true, req.ImageSource, RiskLevel.Low,
            req.CategoryId, req.TestMode, NewHash(), sourceUrl: null,
            rawTitle: req.Title, rawInput: req.RawInput,
            ActorType.AdminUser, createdByRef: "admin", clock);
        item.Schedule(req.ScheduledAt, clock);
        await repository.AddAsync(item, ct);
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>AI'sız: bitmiş metni doğrudan → revizyon oluşturulur → görsel + yayına girer (AI yok).</summary>
    public async Task<Guid> AddWithoutAiAsync(AddManualNoAiRequest req, CancellationToken ct)
    {
        var item = new ContentItem(
            ContentOrigin.ManualNoAi, useAi: false, req.ImageSource, RiskLevel.Low,
            req.CategoryId, req.TestMode, NewHash(), sourceUrl: null,
            rawTitle: req.Title, rawInput: req.BodyHtml,
            ActorType.AdminUser, createdByRef: "admin", clock);

        item.AddRevision(new ContentRevision(
            item.Id, 1, req.Title, req.ShortX, req.BodyHtml, req.InstagramCaption,
            req.Tags, req.PrimaryKeyword, req.ImageAltText, createdBy: "admin", clock));
        item.Schedule(req.ScheduledAt, clock);

        await repository.AddAsync(item, ct);
        await repository.SaveChangesAsync(ct);
        return item.Id;
    }

    private static string NewHash() => $"manual:{Guid.NewGuid():N}";
}
