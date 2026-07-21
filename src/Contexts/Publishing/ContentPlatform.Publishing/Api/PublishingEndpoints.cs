using System.Text.Json;
using ContentPlatform.Publishing.Application;
using ContentPlatform.Publishing.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ContentPlatform.Publishing.Api;

internal static class PublishingEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/publishing").WithTags("Publishing");

        g.MapGet("/channels", (IChannelPublisherRegistry registry) =>
            Results.Ok(new { available = registry.Available.Select(c => c.ToString()) }));

        // Kullanım/maliyet panosu: hangi API ne kadar + toplam $ / ₺
        g.MapGet("/usage", async (int? days, UsageService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(days ?? 30, ct)));

        // Yayın listesi (içeriğe / duruma / aramaya göre, sayfalı)
        g.MapGet("/publications", async (Guid? contentItemId, string? status, string? q, int? page, int? size, IPublicationRepository repo, CancellationToken ct) =>
        {
            PublicationStatus? st = Enum.TryParse<PublicationStatus>(status, true, out var s) ? s : null;
            var p = page ?? 1; var sz = contentItemId is null ? (size ?? 25) : 200; // içerik detayında hepsi
            var (items, total) = await repo.ListPagedAsync(contentItemId, st, q, p, sz, ct);
            return Results.Ok(new { items = items.Select(Dto), page = p, size = sz, total });
        });

        // Yayın detayı (deneme geçmişiyle)
        g.MapGet("/publications/{id:guid}", async (Guid id, IPublicationRepository repo, CancellationToken ct) =>
        {
            var p = await repo.GetAsync(id, ct);
            if (p is null) return Results.NotFound();
            return Results.Ok(new
            {
                Publication = Dto(p),
                Attempts = p.DeliveryAttempts.Select(a => new { a.AttemptNo, Outcome = a.Outcome.ToString(), a.Error, a.CreatedAt })
            });
        });

        // Manuel yeniden dene (dead-letter kurtarma)
        // NOT: İstek iptaline (sekme kapanır/zaman aşımı) BAĞLANMAZ (CancellationToken.None) —
        // gönderim yapıldıysa durumu MUTLAKA kaydedilmeli; yoksa yayın "gitti ama Planlı kaldı"
        // tutarsızlığı oluşur ve planlı saatte İKİNCİ kez gider.
        g.MapPost("/publications/{id:guid}/retry", async (Guid id, IPublicationRepository repo, DistributionService dist) =>
        {
            var ct = CancellationToken.None;
            var p = await repo.GetAsync(id, ct);
            if (p is null) return Results.NotFound();
            if (p.Status == PublicationStatus.Published) return Results.Ok(new { message = "Zaten yayınlanmış." });
            var ok = await dist.PublishOneAsync(p, ct);
            await repo.SaveChangesAsync(ct);
            return Results.Ok(new { published = ok, p.Attempts, p.Error });
        });

        // ---- Planlı yayınlar (zamanı gelmemiş) ----
        g.MapGet("/scheduled", async (IPublicationRepository repo, CancellationToken ct) =>
            Results.Ok((await repo.GetScheduledAsync(200, ct)).Select(Dto)));

        // Yeniden planla (yeni zaman; boş → hemen)
        g.MapPost("/publications/{id:guid}/reschedule", async (Guid id, RescheduleRequest req, IPublicationRepository repo, IClock clock, CancellationToken ct) =>
        {
            var p = await repo.GetAsync(id, ct);
            if (p is null) return Results.NotFound();
            p.Reschedule(req.ScheduledAt, clock);
            await repo.SaveChangesAsync(ct);
            return Results.Ok(Dto(p));
        });

        // Şimdi yayınla — ARKA PLANA DEVRET, kullanıcıyı bekletme.
        // Eskiden burada gönderim INLINE yapılıyordu (dist.PublishOneAsync await) → video işleme/
        // yükleme yüzünden panel isteği dakikalarca (bazen ~2 saat) askıda kalıyordu.
        // Artık: kaydı "hemen due" (Scheduled + ScheduledAt=now) yapıp ANINDA 202 döneriz.
        // Gönderimi ≤1 dk içinde ScheduledDispatchJob (Worker; Worker kapalıysa Api yedek gönderici)
        // atomik sahiplenip yapar. Başarısız olursa mevcut retry (5 deneme, artan gecikme) devreye girer.
        g.MapPost("/publications/{id:guid}/publish-now", async (Guid id, IPublicationRepository repo, IClock clock, CancellationToken ct) =>
        {
            var p = await repo.GetAsync(id, ct);
            if (p is null) return Results.NotFound();
            if (p.Status == PublicationStatus.Published) return Results.Ok(new { message = "Zaten yayınlanmış." });
            p.QueueNow(clock);                       // inline göndermez; arka plana alır
            await repo.SaveChangesAsync(ct);
            return Results.Accepted(
                $"/api/v1/publishing/publications/{id}",
                new { queued = true, status = p.Status.ToString(),
                      message = "Gönderim arka plana alındı; en geç 1 dakika içinde yayınlanacak." });
        });

        // Planı iptal et (bir daha gönderilmez)
        g.MapPost("/publications/{id:guid}/cancel", async (Guid id, IPublicationRepository repo, IClock clock, CancellationToken ct) =>
        {
            var p = await repo.GetAsync(id, ct);
            if (p is null) return Results.NotFound();
            p.Cancel(clock);
            await repo.SaveChangesAsync(ct);
            return Results.Ok(Dto(p));
        });
    }

    private static PublicationDto Dto(Publication p) => new(
        p.Id, p.ContentItemId, p.Channel.ToString(), p.TargetRef, p.Status.ToString(),
        p.ExternalId, p.Attempts, p.Error, p.CreatedAt, p.ScheduledAt, p.PublishedAt, TitleOf(p));

    /// <summary>Panelde "hangi içerik?" görünsün diye başlığı anlık kopyadan (payload) çözer.</summary>
    private static string? TitleOf(Publication p)
    {
        try { return JsonSerializer.Deserialize<PublicationPayload>(p.PayloadJson)?.Title; }
        catch { return null; }
    }
}
