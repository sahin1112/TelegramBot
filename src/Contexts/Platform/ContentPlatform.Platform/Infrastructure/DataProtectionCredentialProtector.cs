using ContentPlatform.Platform.Application;
using Microsoft.AspNetCore.DataProtection;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>Kimlik bilgisi şifreleme — ASP.NET Data Protection (anahtar ring ENV/KMS ile korunur).</summary>
internal sealed class DataProtectionCredentialProtector : ICredentialProtector
{
    private const string Purpose = "SocialAccount.Credentials.v1";
    private readonly IDataProtector _protector;

    public DataProtectionCredentialProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
