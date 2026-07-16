using System.Globalization;
using System.Text;

namespace ContentPlatform.SharedKernel;

/// <summary>
/// SEO dostu "slug" üretimi. Türkçe karakterleri sadeleştirir, küçük harfe indirir,
/// harf/rakam dışını tireye çevirir. Deterministiktir (aynı girdi → aynı çıktı).
/// </summary>
public static class Slug
{
    public static string From(string? text, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) return "yazi";

        var lowered = text.Trim().ToLowerInvariant();
        var map = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            map.Append(ch switch
            {
                'ı' => 'i', 'İ' => 'i', 'ş' => 's', 'ğ' => 'g',
                'ü' => 'u', 'ö' => 'o', 'ç' => 'c',
                _ => ch
            });
        }

        // Kalan aksanları ayrıştırıp at (é→e vb.)
        var normalized = map.ToString().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastDash = false;
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > maxLength) slug = slug[..maxLength].Trim('-');
        return slug.Length == 0 ? "yazi" : slug;
    }
}

/// <summary>
/// Blog gönderisi için kanonik slug. Editorial (link üretimi) ve Site (depolama/route)
/// AYNI formülü kullanır ki Telegram gönderisindeki link ile blog URL'i birebir eşleşsin.
/// Biçim: "{baslik-slug}-{icerik-id-ilk6hex}". Kısa kimlik çakışmayı önler.
/// </summary>
public static class BlogSlug
{
    public static string Build(Guid contentItemId, string? primaryKeyword, string? title)
    {
        var basis = !string.IsNullOrWhiteSpace(primaryKeyword) ? primaryKeyword : title;
        var shortId = contentItemId.ToString("N")[..6];
        return $"{Slug.From(basis)}-{shortId}";
    }
}
