using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Application;
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
        return new AccountCredentials((PlatformKind)account.Platform, values);
    }
}
