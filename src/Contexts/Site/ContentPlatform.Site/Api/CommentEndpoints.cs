using ContentPlatform.Site.Application;
using ContentPlatform.Site.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ContentPlatform.Site.Api;

/// <summary>Yorum uçları: public gönderim (/blog/{slug}/comment) + admin moderasyon (/api/v1/comments).</summary>
internal static class CommentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // ---- Public: yorum gönder (form POST; AuthMiddleware /api/v1 dışı olduğu için serbest) ----
        app.MapPost("/blog/{slug}/comment", async (string slug, HttpRequest req, BlogQueryService q, CommentService comments, CancellationToken ct) =>
        {
            var post = await q.BySlugAsync(slug, ct);
            if (post is null) return Results.NotFound();
            var form = await req.ReadFormAsync(ct);
            var ip = req.HttpContext.Connection.RemoteIpAddress?.ToString();
            await comments.SubmitAsync(post.Id, form["name"], form["email"], form["body"], ip, ct);
            return Results.Redirect($"/blog/{Uri.EscapeDataString(slug)}?ok=1#comments");
        }).DisableAntiforgery().ExcludeFromDescription();

        // ---- Admin: moderasyon ----
        var g = app.MapGroup("/api/v1/comments").WithTags("Comments");

        g.MapGet("/", async (string? status, string? q, int? page, int? size, CommentService comments, CancellationToken ct) =>
        {
            CommentStatus? st = string.IsNullOrEmpty(status) ? CommentStatus.Pending
                : (string.Equals(status, "all", StringComparison.OrdinalIgnoreCase) ? null
                   : (Enum.TryParse<CommentStatus>(status, true, out var s) ? s : CommentStatus.Pending));
            var p = page ?? 1; var sz = size ?? 25;
            var (items, total) = await comments.ListPagedAsync(st, q, p, sz, ct);
            return Results.Ok(new { items, page = p, size = sz, total });
        });

        g.MapPost("/{id:guid}/approve", async (Guid id, CommentService c, CancellationToken ct) =>
            await c.ModerateAsync(id, CommentStatus.Approved, ct) ? Results.Ok() : Results.NotFound());
        g.MapPost("/{id:guid}/reject", async (Guid id, CommentService c, CancellationToken ct) =>
            await c.ModerateAsync(id, CommentStatus.Rejected, ct) ? Results.Ok() : Results.NotFound());
        g.MapPost("/{id:guid}/spam", async (Guid id, CommentService c, CancellationToken ct) =>
            await c.ModerateAsync(id, CommentStatus.Spam, ct) ? Results.Ok() : Results.NotFound());

        // Toplu moderasyon
        g.MapPost("/bulk", async (BulkModerateRequest req, CommentService c, CancellationToken ct) =>
            Results.Ok(new { updated = await c.ModerateManyAsync(req.Ids, req.Status, ct) }));
    }
}
