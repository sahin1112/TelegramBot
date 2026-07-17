using ContentPlatform.Platform.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ContentPlatform.Platform.Api;

/// <summary>Acil durdurma yönetimi (/api/v1/killswitch).</summary>
internal static class KillSwitchEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/killswitch").WithTags("KillSwitch");

        g.MapGet("/", async (IKillSwitchAdmin svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        g.MapPost("/", async (SetKillSwitchRequest req, IKillSwitchAdmin svc, CancellationToken ct) =>
        {
            await svc.SetAsync(req.Scope, req.Key, req.Engaged, req.Reason, ct);
            return Results.Ok();
        });
    }
}
