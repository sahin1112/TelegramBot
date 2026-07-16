using System.Text.RegularExpressions;
using ContentPlatform.Editorial.Application;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Sezgisel kalite kapısı (MVP-lite). Kritik yapısal sorunlar (boş/çok kısa gövde, boş başlık)
/// içeriği tutar; küçük eksikler yalnız uyarıdır. İleride semantik/AI kontrolleriyle güçlendirilebilir.
/// </summary>
internal sealed class ContentQualityGate : IQualityGate
{
    private const int MinBodyChars = 120;      // altında = kritik (boş/başarısız üretim)
    private const int ShortBodyChars = 400;    // altında = uyarı
    private const int MaxShortX = 280;

    public QualityAssessment Evaluate(string? title, string? shortX, string? bodyHtml, IReadOnlyList<string> tags)
    {
        var issues = new List<string>();
        var critical = false;

        if (string.IsNullOrWhiteSpace(title))
        {
            issues.Add("Başlık boş.");
            critical = true;
        }

        var bodyText = StripHtml(bodyHtml);
        if (bodyText.Length < MinBodyChars)
        {
            issues.Add($"Gövde çok kısa ({bodyText.Length} karakter).");
            critical = true;
        }
        else if (bodyText.Length < ShortBodyChars)
        {
            issues.Add($"Gövde kısa ({bodyText.Length} karakter) — değer katıldığından emin olun.");
        }

        if (tags is null || tags.Count == 0)
            issues.Add("Etiket yok.");

        if (string.IsNullOrWhiteSpace(shortX))
            issues.Add("Kısa (X) metni boş.");
        else if (shortX.Length > MaxShortX)
            issues.Add($"Kısa (X) metni sınırı aşıyor ({shortX.Length}>{MaxShortX}).");

        return new QualityAssessment(Passed: issues.Count == 0, Critical: critical, Issues: issues);
    }

    private static string StripHtml(string? html) =>
        string.IsNullOrWhiteSpace(html)
            ? string.Empty
            : Regex.Replace(Regex.Replace(html, "<.*?>", " "), @"\s+", " ").Trim();
}
