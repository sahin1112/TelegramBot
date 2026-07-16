using ContentPlatform.Editorial.Application;
using ContentPlatform.Editorial.Domain;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Anahtar kelime tabanlı risk sınıflandırıcı (MVP-lite). Türkçe sadeleştirilmiş eşleşme.
/// İleride ML/AI sınıflandırıcıyla değiştirilebilir (IRiskClassifier arkasında).
/// </summary>
internal sealed class KeywordRiskClassifier : IRiskClassifier
{
    // Yüksek risk: her zaman insan onayı gerektiren konular.
    private static readonly string[] High =
    {
        "saglik", "hastalik", "kanser", "ilac", "asi", "tedavi", "doz", "korona", "covid", "virus",
        "siyaset", "secim", "cumhurbaskan", "bakan", "milletvekil", "parti ",
        "savas", "teror", "saldiri", "olum", "cinayet", "intihar", "deprem", "afet", "patlama",
        "yatirim tavsiye", "al sat", "hisse oner", "kesin kazanc", "garanti getiri",
        "silah", "guvenlik acig", "hack", "istismar", "dolandiric"
    };

    // Orta risk: dikkat ister ama tavsiye/kriz değil.
    private static readonly string[] Medium =
    {
        "ekonomi", "enflasyon", "zam", "sirket", "ihale", "mahkeme", "dava", "hukuk", "mevzuat",
        "vergi", "banka", "kredi", "faiz", "kripto", "bitcoin", "borsa", "doviz", "kur ", "altin"
    };

    public RiskLevel Classify(string? title, string? text)
    {
        var hay = Normalize($"{title} {text}");
        if (High.Any(k => hay.Contains(k, StringComparison.Ordinal))) return RiskLevel.High;
        if (Medium.Any(k => hay.Contains(k, StringComparison.Ordinal))) return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    /// <summary>Küçük harfe indir + Türkçe karakterleri sadeleştir (anahtar kelimeler ascii).</summary>
    private static string Normalize(string s)
    {
        var lowered = s.ToLowerInvariant();
        var sb = new System.Text.StringBuilder(lowered.Length);
        foreach (var ch in lowered)
            sb.Append(ch switch
            {
                'ı' => 'i', 'İ' => 'i', 'ş' => 's', 'ğ' => 'g', 'ü' => 'u', 'ö' => 'o', 'ç' => 'c',
                _ => ch
            });
        return sb.ToString();
    }
}
