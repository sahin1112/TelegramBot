using ContentPlatform.Ingestion.Application;
using ContentPlatform.Ingestion.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ContentPlatform.Ingestion.Api;

internal static class SourceEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/sources").WithTags("Sources");

        g.MapGet("/", async (ISourceRepository repo, CancellationToken ct) =>
        {
            var list = await repo.ListAsync(ct);
            return Results.Ok(list.Select(Dto));
        });

        g.MapGet("/{id:guid}", async (Guid id, ISourceRepository repo, CancellationToken ct) =>
        {
            var s = await repo.GetAsync(id, ct);
            return s is null ? Results.NotFound() : Results.Ok(Dto(s));
        });

        g.MapPost("/", async (CreateSourceRequest req, ISourceRepository repo, IClock clock, CancellationToken ct) =>
        {
            var source = new Source(req.CategoryId, req.Type, req.Url, req.PollIntervalMinutes, req.Selector, req.IngestSince, clock);
            await repo.AddAsync(source, ct);
            await repo.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/sources/{source.Id}", new { id = source.Id });
        });

        g.MapPut("/{id:guid}", async (Guid id, UpdateSourceRequest req, ISourceRepository repo, IClock clock, CancellationToken ct) =>
        {
            var s = await repo.GetAsync(id, ct);
            if (s is null) return Results.NotFound();
            s.Update(req.CategoryId, req.Url, req.PollIntervalMinutes, req.Selector, req.IngestSince, clock);
            await repo.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // Aç/kapa
        g.MapPost("/{id:guid}/toggle", async (Guid id, ISourceRepository repo, IClock clock, CancellationToken ct) =>
        {
            var s = await repo.GetAsync(id, ct);
            if (s is null) return Results.NotFound();
            if (s.IsActive) s.Disable(clock); else s.Enable(clock);
            await repo.SaveChangesAsync(ct);
            return Results.Ok(new { s.IsActive });
        });

        g.MapDelete("/{id:guid}", async (Guid id, ISourceRepository repo, CancellationToken ct) =>
        {
            var s = await repo.GetAsync(id, ct);
            if (s is null) return Results.NotFound();
            repo.Remove(s);
            await repo.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Test: bir kaynağı KAYDETMEDEN oku, ham öğeleri döndür (selector doğrulama vb.)
        // Dış ağ hataları (DNS/timeout/erişilemeyen site) 500 yerine 502 + açıklayıcı mesajla döner —
        // loglarda "İşlenmeyen hata: POST /api/v1/sources/test" yığınlarını ve 60-90 sn'lik 500'leri önler.
        g.MapPost("/test", async (TestSourceRequest req, IEnumerable<IFeedReader> readers, IClock clock, CancellationToken ct) =>
        {
            var reader = readers.FirstOrDefault(r => r.CanRead(req.Type));
            if (reader is null) return Results.BadRequest("Bu kaynak türü okunamıyor.");
            var probe = new Source(null, req.Type, req.Url, 15, req.Selector, null, clock);
            try
            {
                var items = await reader.ReadAsync(probe, ct);
                return Results.Ok(items.Take(10));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // istemci vazgeçti — normal akış
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem(
                    title: "Kaynağa erişilemedi",
                    detail: $"Kaynak adresine bağlanılamadı ({req.Url}): {ex.Message}",
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (TaskCanceledException)
            {
                return Results.Problem(
                    title: "Kaynak zaman aşımı",
                    detail: $"Kaynak belirlenen sürede yanıt vermedi ({req.Url}). Adresi ve sunucunun dış ağ erişimini kontrol edin.",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
        });

        // Elle tetikle: tüm 'due' kaynakları tarar.
        g.MapPost("/discover", async (DiscoveryService discovery, CancellationToken ct) =>
            Results.Ok(new { discovered = await discovery.DiscoverDueAsync(ct) }));
    }

    private static SourceDto Dto(Source s) => new(s.Id, s.CategoryId, s.Type, s.Url, s.PollIntervalMinutes, s.IsActive, s.LastPolledAt, s.IngestSince);
}
