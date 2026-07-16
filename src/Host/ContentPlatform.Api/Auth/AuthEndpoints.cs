using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Api.Auth;

public sealed record LoginRequest(string Username, string Password);

internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/auth").WithTags("Auth");

        g.MapPost("/login", (LoginRequest req, IOptions<AuthOptions> opt, PasswordHasher hasher, AuthTokenService tokens) =>
        {
            var o = opt.Value;
            var ok = string.Equals(req.Username?.Trim(), o.Username, StringComparison.OrdinalIgnoreCase)
                     && hasher.Verify(req.Password ?? "", o.PasswordSalt, o.PasswordHash);
            if (!ok) return Results.Unauthorized();
            var token = tokens.Create(o.Username, o.TokenTtlHours);
            return Results.Ok(new { token, expiresInHours = o.TokenTtlHours });
        });

        // Token geçerli mi? (SPA açılışta kontrol eder)
        g.MapGet("/me", (HttpContext ctx, AuthTokenService tokens) =>
        {
            var user = tokens.Validate(Bearer(ctx));
            return user is null ? Results.Unauthorized() : Results.Ok(new { user });
        });
    }

    private static string? Bearer(HttpContext ctx)
    {
        var h = ctx.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h["Bearer ".Length..] : null;
    }
}
