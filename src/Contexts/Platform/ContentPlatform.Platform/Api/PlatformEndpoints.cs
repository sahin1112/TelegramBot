using ContentPlatform.Platform.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ContentPlatform.Platform.Api;

/// <summary>Entegrasyonlar arayüzden: hesap oluştur, hedef ekle, listele. Kimlik ASLA düz dönmez.</summary>
internal static class PlatformEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/social-accounts").WithTags("SocialAccounts");

        g.MapGet("/", async (SocialAccountService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        g.MapPost("/", async (CreateSocialAccountRequest req, SocialAccountService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateAsync(req, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/social-accounts/{result.Value}", new { id = result.Value })
                : Results.BadRequest(result.Error);
        });

        g.MapPost("/{id:guid}/targets", async (Guid id, AddTargetRequest req, SocialAccountService svc, CancellationToken ct) =>
        {
            var result = await svc.AddTargetAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        g.MapGet("/{id:guid}", async (Guid id, SocialAccountService svc, CancellationToken ct) =>
        {
            var d = await svc.GetDetailAsync(id, ct);
            return d is null ? Results.NotFound() : Results.Ok(d);
        });

        g.MapPost("/{id:guid}/disable", async (Guid id, SocialAccountService svc, CancellationToken ct) =>
        {
            var r = await svc.DisableAccountAsync(id, ct);
            return r.IsSuccess ? Results.Ok() : Results.NotFound(r.Error);
        });

        g.MapPost("/targets/{targetId:guid}/disable", async (Guid targetId, SocialAccountService svc, CancellationToken ct) =>
        {
            var r = await svc.DisableTargetAsync(targetId, ct);
            return r.IsSuccess ? Results.Ok() : Results.NotFound(r.Error);
        });

        // ---- Global ayarlar (API anahtarları/token/fiyat/kur) ----
        var s2 = app.MapGroup("/api/v1/settings").WithTags("Settings");
        s2.MapGet("/", async (SettingsService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        s2.MapPut("/", async (SetSettingRequest req, SettingsService svc, CancellationToken ct) =>
        {
            await svc.SetAsync(req.Key, req.Value, req.IsSecret, ct);
            return Results.Ok();
        });
    }
}
