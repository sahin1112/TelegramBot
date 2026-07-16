using ContentPlatform.Platform.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ContentPlatform.Platform.Api;

internal static class CategoryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/categories").WithTags("Categories");

        g.MapGet("/", async (CategoryService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));

        g.MapPost("/", async (CreateCategoryRequest req, CategoryService svc, CancellationToken ct) =>
        {
            var r = await svc.CreateAsync(req, ct);
            return r.IsSuccess ? Results.Created($"/api/v1/categories/{r.Value}", new { id = r.Value }) : Results.BadRequest(r.Error);
        });

        g.MapPut("/{id:guid}", async (Guid id, UpdateCategoryRequest req, CategoryService svc, CancellationToken ct) =>
        {
            var r = await svc.UpdateAsync(id, req, ct);
            return r.IsSuccess ? Results.Ok() : Results.NotFound(r.Error);
        });

        g.MapPost("/{id:guid}/toggle", async (Guid id, CategoryService svc, CancellationToken ct) =>
        {
            var r = await svc.ToggleAsync(id, ct);
            return r.IsSuccess ? Results.Ok() : Results.NotFound(r.Error);
        });

        g.MapDelete("/{id:guid}", async (Guid id, CategoryService svc, CancellationToken ct) =>
        {
            var r = await svc.DeleteAsync(id, ct);
            return r.IsSuccess ? Results.NoContent() : Results.NotFound(r.Error);
        });
    }
}
