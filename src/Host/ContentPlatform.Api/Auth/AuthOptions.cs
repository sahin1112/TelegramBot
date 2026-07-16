namespace ContentPlatform.Api.Auth;

/// <summary>Tek kullanıcı admin girişi. Şifre PBKDF2 hash olarak saklanır (düz metin değil).</summary>
public sealed class AuthOptions
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";   // base64
    public string PasswordSalt { get; set; } = "";   // base64
    public int TokenTtlHours { get; set; } = 24;
}
