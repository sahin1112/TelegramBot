using System.Text.Json;
using ContentPlatform.SharedKernel;
using Microsoft.AspNetCore.DataProtection;

namespace ContentPlatform.Api.Auth;

/// <summary>Data Protection ile imzalı, süreli bearer token üretir/doğrular (stateless, ek paket yok).</summary>
public sealed class AuthTokenService
{
    private readonly IDataProtector _protector;
    private readonly IClock _clock;

    public AuthTokenService(IDataProtectionProvider provider, IClock clock)
    {
        _protector = provider.CreateProtector("AdminAuth.Token.v1");
        _clock = clock;
    }

    public string Create(string username, int ttlHours)
    {
        var payload = JsonSerializer.Serialize(new Payload(username, _clock.UtcNow.AddHours(ttlHours)));
        return _protector.Protect(payload);
    }

    public string? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var p = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
            return p is not null && p.Exp > _clock.UtcNow ? p.User : null;
        }
        catch { return null; }
    }

    private sealed record Payload(string User, DateTimeOffset Exp);
}
