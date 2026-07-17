using ContentPlatform.Editorial.Application;
using ContentPlatform.Editorial.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ContentPlatform.Editorial.Api;

internal static class EditorialEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/editorial").WithTags("Editorial");

        // ---- Onay kuyruğu (RSS/sayfa ham içerikleri — AI'sız) ----
        g.MapGet("/queue", async (IContentRepository repo, CancellationToken ct) =>
        {
            var items = await repo.GetByStatusAsync(EditorialStatus.PendingReview, 50, ct);
            return Results.Ok(items.Select(Summary));
        });

        // ---- İçerik listesi (durum filtreli, sayfalı) ----
        g.MapGet("/", async (string? status, string? q, int? page, int? size, IContentRepository repo, CancellationToken ct) =>
        {
            EditorialStatus? st = Enum.TryParse<EditorialStatus>(status, true, out var s) ? s : null;
            var (items, total) = await repo.GetPagedAsync(st, q, page ?? 1, size ?? 20, ct);
            return Results.Ok(new PagedContentDto(items.Select(Summary).ToList(), page ?? 1, size ?? 20, total));
        });

        // ---- İçerik detayı ----
        g.MapGet("/{id:guid}", async (Guid id, IContentRepository repo, CancellationToken ct) =>
        {
            var i = await repo.GetAsync(id, ct);
            return i is null ? Results.NotFound() : Results.Ok(Detail(i));
        });

        // ---- Onayla (opsiyonel görsel kaynağı seçimiyle) → AI üretimi tetiklenir ----
        g.MapPost("/{id:guid}/approve", async (Guid id, ApproveRequest? req, IContentRepository repo, IContentAudit audit, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            if (req?.ImageSource is { } src) item.SetImageSource(src, clock);
            if (req?.TestMode is { } tm) item.SetTestMode(tm, clock);
            item.Schedule(req?.ScheduledAt, clock); // null → kategori politikası / hemen
            var r = item.Approve("admin", automated: false, clock); // panelden = insan onayı
            if (r.IsFailure) return Results.Conflict(r.Error);
            audit.Log(id, Domain.AuditEvent.Approved, Domain.ActorType.AdminUser, "admin");
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // ---- İçerik zaman çizelgesi (izlenebilirlik) ----
        g.MapGet("/{id:guid}/audit", async (Guid id, IContentAudit audit, CancellationToken ct) =>
            Results.Ok((await audit.GetTimelineAsync(id, ct))
                .Select(a => new AuditDto(a.Event, a.ActorType, a.ActorRef, a.Detail, a.CreatedAt))));

        // ---- Yayınla (kalite kapısında tutulan / hazır içeriği elle yayına gönder) ----
        g.MapPost("/{id:guid}/publish", async (Guid id, ContentGenerationService gen, CancellationToken ct) =>
        {
            var r = await gen.PublishExistingAsync(id, ct);
            return r.IsSuccess ? Results.Ok() : Results.Conflict(r.Error);
        });

        // ---- Panelden AI ile üret: TÜM alanlar (yeni revizyon) ----
        g.MapPost("/{id:guid}/generate", async (Guid id, GenerateDraftRequest? req, ContentGenerationService gen, CancellationToken ct) =>
        {
            var r = await gen.GenerateDraftAsync(id, req?.SeedInput, ct);
            return r.IsSuccess ? Results.Ok() : Results.Conflict(r.Error);
        });

        // ---- Panelden AI ile üret: TEK alan (kaydetmez, değeri döndürür) ----
        g.MapPost("/generate-field", async (GenerateFieldRequest req, ContentGenerationService gen, CancellationToken ct) =>
        {
            var r = await gen.GenerateFieldAsync(req, ct);
            return r.IsSuccess ? Results.Ok(new { value = r.Value }) : Results.Conflict(r.Error);
        });

        // ---- Reddet (0 maliyet) ----
        g.MapPost("/{id:guid}/reject", async (Guid id, IContentRepository repo, IContentAudit audit, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            var r = item.Reject("admin", clock);
            if (r.IsFailure) return Results.Conflict(r.Error);
            audit.Log(id, Domain.AuditEvent.Rejected, Domain.ActorType.AdminUser, "admin");
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // ---- Onaya gönder (Taslak → onay kuyruğu). Güncel revizyon şart. ----
        g.MapPost("/{id:guid}/submit-review", async (Guid id, IContentRepository repo, IContentAudit audit, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            var r = item.SubmitForReview(clock);
            if (r.IsFailure) return Results.Conflict(r.Error);
            audit.Log(id, Domain.AuditEvent.Edited, Domain.ActorType.AdminUser, "admin", "Onaya gönderildi");
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // ---- Toplu onay ----
        g.MapPost("/bulk-approve", async (BulkApproveRequest req, IContentRepository repo, IContentAudit audit, IClock clock, CancellationToken ct) =>
        {
            var ok = 0;
            foreach (var id in req.Ids)
            {
                var item = await repo.GetAsync(id, ct);
                if (item is not null && item.Approve("admin", automated: false, clock).IsSuccess)
                {
                    audit.Log(id, Domain.AuditEvent.Approved, Domain.ActorType.AdminUser, "admin", "toplu onay");
                    ok++;
                }
            }
            await repo.SaveChangesAsync(ct);
            return Results.Ok(new { approved = ok, total = req.Ids.Count });
        });

        // ---- Test moduna al/çıkar ----
        g.MapPost("/{id:guid}/testmode", async (Guid id, TestModeRequest req, IContentRepository repo, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            item.SetTestMode(req.Enabled, clock);
            await repo.SaveChangesAsync(ct);
            return Results.Ok(new { item.TestMode });
        });

        // ---- Metni düzenle (boşluk/paragraf korunur) → yeni revizyon ----
        g.MapPut("/{id:guid}/revision", async (Guid id, EditRevisionRequest req, IContentRepository repo, IContentAudit audit, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            item.AddRevision(new Domain.ContentRevision(
                item.Id, item.Revisions.Count + 1, req.Title, req.ShortX, req.BodyHtml, req.InstagramCaption,
                req.Tags, req.PrimaryKeyword, req.ImageAltText, createdBy: "admin-edit", clock));
            audit.Log(id, Domain.AuditEvent.Edited, Domain.ActorType.AdminUser, "admin");
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // ---- Geri çek / arşivle ----
        g.MapPost("/{id:guid}/retract", async (Guid id, IContentRepository repo, IContentAudit audit, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            item.Archive(clock);
            audit.Log(id, Domain.AuditEvent.Retracted, Domain.ActorType.AdminUser, "admin");
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // ---- Manuel içerik ekle (AI'lı → taslak ürettir) ----
        g.MapPost("/manual", async (AddManualAiRequest req, ManualContentService svc, CancellationToken ct) =>
            Results.Created($"/api/v1/editorial/{await svc.AddWithAiAsync(req, ct)}", null));

        // ---- Metin yapıştır → AI HEMEN özgün içerik üretsin → önizleme (PendingReview, otomatik yayınlanmaz) ----
        g.MapPost("/manual-generate", async (AddManualAiRequest req, ManualContentService svc, ContentGenerationService gen, CancellationToken ct) =>
        {
            var id = await svc.AddForReviewAsync(req, ct);
            var r = await gen.GenerateDraftAsync(id, req.RawInput, ct); // seed = yapıştırılan metin
            return Results.Ok(new { id, ok = r.IsSuccess, message = r.IsSuccess ? null : r.Error.Message });
        });

        // ---- Manuel içerik ekle (AI'sız → bitmiş metin) ----
        g.MapPost("/manual-no-ai", async (AddManualNoAiRequest req, ManualContentService svc, CancellationToken ct) =>
            Results.Created($"/api/v1/editorial/{await svc.AddWithoutAiAsync(req, ct)}", null));

        // ---- Görsel bekleyenler + yükleme ("Ben yükleyeceğim") ----
        g.MapGet("/pending-image", async (IContentRepository repo, CancellationToken ct) =>
        {
            var items = await repo.GetAwaitingManualImageAsync(50, ct);
            return Results.Ok(items.Select(i => new { i.Id, Title = CurrentTitle(i), i.CreatedAt }));
        });

        g.MapPost("/{id:guid}/image", async (Guid id, IFormFile file, ContentGenerationService gen, CancellationToken ct) =>
        {
            if (file.Length == 0) return Results.BadRequest("Boş dosya.");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var r = await gen.AttachManualImageAsync(id, ms.ToArray(), file.ContentType, ct);
            return r.IsSuccess ? Results.Ok() : Results.BadRequest(r.Error);
        }).DisableAntiforgery();

        // ---- Detay ekranı: görseli HEMEN üret (AI/SkiaCard) — YAYINLAMAZ, önizleme URL'si döner ----
        g.MapPost("/{id:guid}/preview-image", async (Guid id, PreviewImageRequest req, ContentGenerationService gen, CancellationToken ct) =>
        {
            var r = await gen.GeneratePreviewImageAsync(id, req.ImageSource, ct);
            return r.IsSuccess ? Results.Ok(new { url = r.Value }) : Results.Conflict(r.Error);
        });

        // ---- Detay ekranı: görseli elle yükle — YAYINLAMAZ, önizleme URL'si döner ----
        g.MapPost("/{id:guid}/preview-image/upload", async (Guid id, IFormFile file, ContentGenerationService gen, CancellationToken ct) =>
        {
            if (file.Length == 0) return Results.BadRequest("Boş dosya.");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var r = await gen.AttachPreviewImageAsync(id, ms.ToArray(), file.ContentType, ct);
            return r.IsSuccess ? Results.Ok(new { url = r.Value }) : Results.BadRequest(r.Error);
        }).DisableAntiforgery();
    }

    private static string? CurrentTitle(ContentItem i) =>
        i.Revisions.FirstOrDefault(r => r.IsCurrent)?.Title ?? i.RawTitle;

    private static ContentSummaryDto Summary(ContentItem i) => new(
        i.Id, i.Origin, i.EditorialStatus, i.MediaStatus, i.RiskLevel, CurrentTitle(i), i.CreatedAt);

    private static ContentDetailDto Detail(ContentItem i)
    {
        var rev = i.Revisions.FirstOrDefault(r => r.IsCurrent);
        var media = i.Media.LastOrDefault();
        return new ContentDetailDto(
            i.Id, i.Origin, i.EditorialStatus, i.MediaStatus, i.RiskLevel, i.ImageSource, i.TestMode, i.CategoryId,
            rev?.Title ?? i.RawTitle, rev?.ShortX, rev?.BodyHtml, rev?.InstagramCaption,
            rev?.Tags ?? new List<string>(), media?.Url, i.CreatedAt, i.ScheduledAt, i.PublishedAt, i.Error, i.RawInput);
    }
}
