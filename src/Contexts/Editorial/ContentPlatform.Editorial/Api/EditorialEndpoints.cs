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
        g.MapGet("/", async (string? status, int? page, int? size, IContentRepository repo, CancellationToken ct) =>
        {
            EditorialStatus? st = Enum.TryParse<EditorialStatus>(status, true, out var s) ? s : null;
            var (items, total) = await repo.GetPagedAsync(st, page ?? 1, size ?? 20, ct);
            return Results.Ok(new PagedContentDto(items.Select(Summary).ToList(), page ?? 1, size ?? 20, total));
        });

        // ---- İçerik detayı ----
        g.MapGet("/{id:guid}", async (Guid id, IContentRepository repo, CancellationToken ct) =>
        {
            var i = await repo.GetAsync(id, ct);
            return i is null ? Results.NotFound() : Results.Ok(Detail(i));
        });

        // ---- Onayla (opsiyonel görsel kaynağı seçimiyle) → AI üretimi tetiklenir ----
        g.MapPost("/{id:guid}/approve", async (Guid id, ApproveRequest? req, IContentRepository repo, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            if (req?.ImageSource is { } src) item.SetImageSource(src, clock);
            if (req?.TestMode is { } tm) item.SetTestMode(tm, clock);
            item.Schedule(req?.ScheduledAt, clock); // null → kategori politikası / hemen
            var r = item.Approve("admin", clock);
            if (r.IsFailure) return Results.Conflict(r.Error);
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // ---- Reddet (0 maliyet) ----
        g.MapPost("/{id:guid}/reject", async (Guid id, IContentRepository repo, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            var r = item.Reject("admin", clock);
            if (r.IsFailure) return Results.Conflict(r.Error);
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // ---- Toplu onay ----
        g.MapPost("/bulk-approve", async (BulkApproveRequest req, IContentRepository repo, IClock clock, CancellationToken ct) =>
        {
            var ok = 0;
            foreach (var id in req.Ids)
            {
                var item = await repo.GetAsync(id, ct);
                if (item is not null && item.Approve("admin", clock).IsSuccess) ok++;
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
        g.MapPut("/{id:guid}/revision", async (Guid id, EditRevisionRequest req, IContentRepository repo, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            item.AddRevision(new Domain.ContentRevision(
                item.Id, item.Revisions.Count + 1, req.Title, req.ShortX, req.BodyHtml, req.InstagramCaption,
                req.Tags, req.PrimaryKeyword, req.ImageAltText, createdBy: "admin-edit", clock));
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // ---- Geri çek / arşivle ----
        g.MapPost("/{id:guid}/retract", async (Guid id, IContentRepository repo, IClock clock, CancellationToken ct) =>
        {
            var item = await repo.GetAsync(id, ct);
            if (item is null) return Results.NotFound();
            item.Archive(clock);
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // ---- Manuel içerik ekle (AI'lı → taslak ürettir) ----
        g.MapPost("/manual", async (AddManualAiRequest req, ManualContentService svc, CancellationToken ct) =>
            Results.Created($"/api/v1/editorial/{await svc.AddWithAiAsync(req, ct)}", null));

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
            rev?.Tags ?? new List<string>(), media?.Url, i.CreatedAt, i.ScheduledAt, i.PublishedAt);
    }
}
