using ContentPlatform.Abstractions;
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

        // ---- HERKESE AÇIK: görseli JPEG'e çevirerek sunar (/media-jpg/{dosya}) ----
        // Instagram image_url YALNIZ JPEG kabul eder; SkiaCard/AI görselleri PNG üretilir.
        // InstagramPublisher PNG/WebP URL'lerini bu uca çevirir; uç dosyayı okur, JPEG'e kodlar.
        // /api/v1 dışı olduğu için auth istemez (Instagram sunucusu erişebilmeli).
        app.MapGet("/media-jpg/{file}", async (string file, IMediaReader mediaReader, CancellationToken ct) =>
        {
            if (file.Contains('/') || file.Contains('\\') || file.Contains("..")) return Results.NotFound();
            var m = await mediaReader.TryReadAsync(file, ct);
            if (m is null) return Results.NotFound();
            if (m.ContentType == "image/jpeg") return Results.File(m.Bytes, "image/jpeg");
            using var bmp = SkiaSharp.SKBitmap.Decode(m.Bytes);
            if (bmp is null) return Results.NotFound();
            using var img = SkiaSharp.SKImage.FromBitmap(bmp);
            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
            return Results.File(data.ToArray(), "image/jpeg");
        }).ExcludeFromDescription();

        // ---- Onay kuyruğu (RSS/sayfa ham içerikleri — AI'sız) ----
        g.MapGet("/queue", async (IContentRepository repo, CancellationToken ct) =>
        {
            var items = await repo.GetByStatusAsync(EditorialStatus.PendingReview, 50, ct);
            return Results.Ok(items.Select(Summary));
        });

        // ---- İçerik listesi (durum filtreli, sayfalı) ----
        g.MapGet("/", async (string? status, string? q, int? page, int? size, bool? asc, IContentRepository repo, CancellationToken ct) =>
        {
            EditorialStatus? st = Enum.TryParse<EditorialStatus>(status, true, out var s) ? s : null;
            var (items, total) = await repo.GetPagedAsync(st, q, page ?? 1, size ?? 20, asc ?? false, ct);
            return Results.Ok(new PagedContentDto(items.Select(Summary).ToList(), page ?? 1, size ?? 20, total));
        });

        // ---- İçerik detayı ----
        g.MapGet("/{id:guid}", async (Guid id, IContentRepository repo, CancellationToken ct) =>
        {
            var i = await repo.GetAsync(id, ct);
            return i is null ? Results.NotFound() : Results.Ok(Detail(i));
        });

        // ---- Onayla (opsiyonel görsel kaynağı seçimiyle) → AI üretimi tetiklenir ----
        g.MapPost("/{id:guid}/approve", async (Guid id, ApproveRequest? req, IContentRepository repo, IContentAudit audit, ContentGenerationService gen, IClock clock, CancellationToken ct) =>
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

            // Metin (revizyon) VE görsel zaten hazırsa Worker'ı beklemeden HEMEN yayına gönder.
            // (Üretim sorgusu bu durumu almaz; tetiklenmezse içerik sessizce takılırdı.)
            bool? published = null; string? note = null;
            if (item.MediaStatus == MediaStatus.Ready && item.Revisions.Any(rv => rv.IsCurrent))
            {
                var pr = await gen.PublishExistingAsync(id, adGate: false, ct);
                published = pr.IsSuccess;
                if (pr.IsFailure) note = pr.Error.Message;
            }
            return Results.Ok(new { published, note });
        });

        // ---- İçerik zaman çizelgesi (izlenebilirlik) ----
        g.MapGet("/{id:guid}/audit", async (Guid id, IContentAudit audit, CancellationToken ct) =>
            Results.Ok((await audit.GetTimelineAsync(id, ct))
                .Select(a => new AuditDto(a.Event, a.ActorType, a.ActorRef, a.Detail, a.CreatedAt))));

        // ---- Yayınla (kalite kapısında tutulan / hazır içeriği elle yayına gönder) ----
        // Tek seferlik TEST gönderimi: test hedeflerine HEMEN yayınlar; asıl yayın akışını değiştirmez.
        g.MapPost("/{id:guid}/send-test", async (Guid id, ContentGenerationService gen, CancellationToken ct) =>
        {
            var r = await gen.SendTestAsync(id, ct);
            return r.IsSuccess ? Results.Ok() : Results.BadRequest(r.Error);
        });

        g.MapPost("/{id:guid}/publish", async (Guid id, bool? adGate, ContentGenerationService gen, CancellationToken ct) =>
        {
            var r = await gen.PublishExistingAsync(id, adGate ?? false, ct);
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
        g.MapPost("/bulk-approve", async (BulkApproveRequest req, IContentRepository repo, IContentAudit audit, ContentGenerationService gen, IClock clock, CancellationToken ct) =>
        {
            var ok = 0; var publishedNow = 0;
            foreach (var id in req.Ids)
            {
                var item = await repo.GetAsync(id, ct);
                if (item is not null && item.Approve("admin", automated: false, clock).IsSuccess)
                {
                    audit.Log(id, Domain.AuditEvent.Approved, Domain.ActorType.AdminUser, "admin", "toplu onay");
                    ok++;
                    await repo.SaveChangesAsync(ct);
                    // Metin + görsel hazırsa bekletmeden yayına gönder (tekil onayla aynı kural).
                    if (item.MediaStatus == MediaStatus.Ready && item.Revisions.Any(rv => rv.IsCurrent)
                        && (await gen.PublishExistingAsync(id, adGate: false, ct)).IsSuccess)
                        publishedNow++;
                }
            }
            await repo.SaveChangesAsync(ct);
            return Results.Ok(new { approved = ok, total = req.Ids.Count, publishedNow });
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
        // Video arka plan müziği yükle (mp3) — URL döner; panel bunu 'video.music_url' ayarına yazar.
        g.MapPost("/video-music", async (IFormFile file, IMediaStore store, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0) return Results.BadRequest(new { message = "Dosya boş." });
            if (file.Length > 10_000_000) return Results.BadRequest(new { message = "En fazla 10 MB." });
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var url = await store.SaveAsync(ms.ToArray(), "audio/mpeg", ct);
            return Results.Ok(new { url });
        }).DisableAntiforgery();

        // Reels/Shorts/TikTok slayt videosu üret (cümle bütünlüklü sayfalar × 7 sn) — önizleme URL'i döner.
        // Gövde opsiyonel: {style: 0..19} şablon seçer; gövdesiz/boş = RASTGELE şablon.
        g.MapPost("/{id:guid}/preview-video", async (Guid id, PreviewVideoRequest? req, ContentGenerationService gen, CancellationToken ct) =>
        {
            var r = await gen.GeneratePreviewVideoAsync(id, req?.Style, ct);
            return r.IsSuccess ? Results.Ok(new { url = r.Value }) : Results.BadRequest(r.Error);
        });

        g.MapPost("/{id:guid}/preview-image", async (Guid id, PreviewImageRequest req, ContentGenerationService gen, CancellationToken ct) =>
        {
            var r = await gen.GeneratePreviewImageAsync(id, req.ImageSource, req.CardStyle, ct);
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
        var media = i.Media.LastOrDefault(m => m.Kind != Domain.MediaKind.Video); // görsel (video hariç)
        var video = i.Media.LastOrDefault(m => m.Kind == Domain.MediaKind.Video); // Reels/Shorts videosu
        return new ContentDetailDto(
            i.Id, i.Origin, i.EditorialStatus, i.MediaStatus, i.RiskLevel, i.ImageSource, i.TestMode, i.CategoryId,
            rev?.Title ?? i.RawTitle, rev?.ShortX, rev?.BodyHtml, rev?.InstagramCaption,
            rev?.Tags ?? new List<string>(), media?.Url, video?.Url, i.CreatedAt, i.ScheduledAt, i.PublishedAt, i.Error, i.RawInput);
    }
}
