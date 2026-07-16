using Microsoft.AspNetCore.Http;

namespace ContentPlatform.Api.Auth;

/// <summary>/api/v1/* uçlarını korur (login hariç). Statik admin dosyaları ve /health serbest.</summary>
public sealed class AuthMiddleware(RequestDelegate next, AuthTokenService tokens)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        var isApi = path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase);
        var isAuth = path.StartsWith("/api/v1/auth/", StringComparison.OrdinalIgnoreCase);

        if (isApi && !isAuth)
        {
            var header = ctx.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..] : null;
            if (tokens.Validate(token) is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { message = "Yetkisiz. Lütfen giriş yapın." });
                return;
            }
        }
        await next(ctx);
    }
}
