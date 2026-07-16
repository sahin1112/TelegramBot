using System.Security.Cryptography;
using System.Text;

namespace ContentPlatform.Api.Auth;

/// <summary>PBKDF2 (SHA256, 100k iterasyon) ile şifre doğrulama; sabit-zaman karşılaştırma.</summary>
public sealed class PasswordHasher
{
    private const int Iterations = 100_000;

    public bool Verify(string password, string saltB64, string hashB64)
    {
        if (string.IsNullOrEmpty(saltB64) || string.IsNullOrEmpty(hashB64)) return false;
        var salt = Convert.FromBase64String(saltB64);
        var expected = Convert.FromBase64String(hashB64);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
