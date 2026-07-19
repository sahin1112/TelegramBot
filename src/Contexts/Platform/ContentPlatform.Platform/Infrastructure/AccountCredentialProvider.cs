using System.Text.Json;
using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Application;
using ContentPlatform.SharedKernel;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>Hesabın şifreli kimliğini çözüp adaptöre verir (yalnız yayın anında, in-memory).</summary>
internal sealed class AccountCredentialProvider(ISocialAccountRepository repository, SocialAccountService service)
    : IAccountCredentialProvider
{
    public async Task<AccountCredentials?> GetAsync(Guid socialAccountId, CancellationToken ct)
    {
        var account = await repository.GetAsync(socialAccountId, ct);
        if (account is null) return null;
        var values = service.DecryptCredentials(account);
        return new AccountCredentials((PlatformKind)account.Platform, values, account.Id);
    }
}

/// <summary>
/// Adaptörlerin yenilediği token'ları kalıcı kayda geri yazar (yeniden şifreleyerek).
/// X'te refresh token TEK KULLANIMLIK olduğundan bu kayıt hayati — yazılmazsa bağlantı kopar.
/// </summary>
internal sealed class CredentialUpdater(
    ISocialAccountRepository repository,
    ICredentialProtector protector,
    IClock clock) : ICredentialUpdater
{
    public async Task UpdateAsync(Guid socialAccountId, IReadOnlyDictionary<string, string> values, DateTimeOffset? tokenExpiresAt, CancellationToken ct)
    {
        var account = await repository.GetAsync(socialAccountId, ct);
        if (account is null) return;
        var encrypted = protector.Protect(JsonSerializer.Serialize(values));
        account.UpdateToken(encrypted, tokenExpiresAt ?? account.TokenExpiresAt ?? clock.UtcNow.AddHours(2), clock);
        await repository.SaveChangesAsync(ct);
    }
}
