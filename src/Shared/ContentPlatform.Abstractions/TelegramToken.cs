using System.Text.RegularExpressions;

namespace ContentPlatform.Abstractions;

/// <summary>
/// Telegram bot token biçim denetimi — TEK yerde. BotFather token'ı "123456789:AA..." biçimindedir
/// (rakamlar + ':' + en az 30 karakterlik [A-Za-z0-9_-] gövde). Panele yanlışlıkla şifre/rastgele
/// metin girilirse ("159753xX?*" vakası) API'ye hiç istek atılmaz; loglar 404 ile dolmaz.
/// </summary>
public static class TelegramToken
{
    private static readonly Regex Pattern = new(@"^\d{5,16}:[A-Za-z0-9_-]{30,}$", RegexOptions.Compiled);

    public static bool LooksValid(string? token) =>
        !string.IsNullOrWhiteSpace(token) && Pattern.IsMatch(token.Trim());

    /// <summary>Log için güvenli maske: ilk 6 + son 3 karakter, arası "…" (token sızdırılmaz).</summary>
    public static string Mask(string? token)
    {
        if (string.IsNullOrEmpty(token)) return "(boş)";
        return token.Length <= 9 ? new string('*', token.Length) : $"{token[..6]}…{token[^3..]}";
    }
}
