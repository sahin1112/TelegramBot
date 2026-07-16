namespace ContentPlatform.Platform.Application;

/// <summary>Kimlik bilgisi şifreleme portu (uygulama ASP.NET Data Protection ile yapılır).</summary>
public interface ICredentialProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
