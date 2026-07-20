using System.Text.RegularExpressions;
using ContentPlatform.Editorial.Application;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Sezgisel kalite kapısı (MVP-lite). Kritik sorunlar içeriği İNSAN incelemesine düşürür (yayınlanmaz):
/// boş/çok kısa gövde, boş başlık, AŞIRI TEKRAR (aynı fikirlerin dönmesi), kaynağa gönderme yapan
/// META cümleler ("kaynak metin belirtmiyor" — habere girmez) ve kaynaktaki sayısal ayrıntıların
/// TAMAMEN atlanması. Küçük eksikler yalnız uyarıdır. Amaç: "özgünleşmiş ama eti kemiği eksilmiş /
/// iki fikri on paragrafa yaymış" içeriğin otomatik yayınlanmasını engellemek.
/// </summary>
internal sealed class ContentQualityGate : IQualityGate
{
    private const int MinBodyChars = 120;      // altında = kritik (boş/başarısız üretim)
    private const int ShortBodyChars = 400;    // altında = uyarı
    private const int MaxShortX = 280;
    private const double RepetitionWarn = 0.10;     // 5-kelimelik pencere tekrar oranı: uyarı eşiği
    private const double RepetitionCritical = 0.22; // kritik eşik — metin belirgin biçimde kendini tekrarlıyor

    public QualityAssessment Evaluate(string? title, string? shortX, string? bodyHtml, IReadOnlyList<string> tags, string? sourceText = null)
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

        // ---- TEKRAR: aynı cümle/fikirlerin farklı kelimelerle dönmesi (şişirme) ----
        var rep = RepetitionRatio(bodyText);
        if (rep >= RepetitionCritical)
        {
            issues.Add($"Gövde aşırı tekrarlı (~%{(int)(rep * 100)}) — aynı ifadeler dönüyor; paragraflar yeni bilgi eklemeli.");
            critical = true;
        }
        else if (rep >= RepetitionWarn)
        {
            issues.Add($"Gövdede tekrar var (~%{(int)(rep * 100)}) — şişirme izlenimi verebilir.");
        }

        // ---- META CÜMLE: makale kendi kaynağından söz ediyor ("kaynak metin ... açıklamıyor") ----
        // Okuyucu kaynağı bilmez; bu cümleler hem anlamsız hem de çoğu zaman YANLIŞTIR
        // (bilgi kaynakta olduğu halde model "belirtilmemiş" diyebiliyor).
        var meta = FindMetaSourceSentence(bodyText);
        if (meta is not null)
        {
            issues.Add("Kaynağa gönderme yapan meta cümle: \"" + Truncate(meta, 110) + "\"");
            critical = true;
        }

        // ---- BİREBİR ALINTI (TELİF): kaynakla ortak 7+ kelimelik dizi ----
        // Aynı dildeki kaynaktan cümle kopyalanmışsa (çeviri değil, birebir) kopya tespit araçları
        // yakalar; 7+ kelimelik ortak dizi kritik sayılır ve içerik incelemeye düşer.
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            var lifted = FindCopiedRun(sourceText!, bodyText, 7);
            if (lifted is not null)
            {
                issues.Add("Kaynaktan birebir alıntı (7+ kelime): \"" + Truncate(lifted, 110) + "\"");
                critical = true;
            }
        }

        // ---- KELİME SAYISI: karakter eşiğini geçse de SEO için ince içerik uyarısı ----
        var wordCount = Regex.Matches(bodyText, @"[\p{L}\p{Nd}]+").Count;
        if (bodyText.Length >= MinBodyChars && wordCount < 250)
            issues.Add($"Gövde SEO için ince ({wordCount} kelime) — kaynaktaki olguların tamamı işlendi mi?");

        // ---- SAYISAL SADAKAT: kaynaktaki somut sayıların metne taşınması ----
        // Kaynakta 4+ farklı sayı varken metinde HİÇBİRİ yoksa somut bilgiler atlanmış demektir.
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            var srcNums = NumberTokens(sourceText!);
            if (srcNums.Count >= 4)
            {
                var bodyNums = NumberTokens((title ?? "") + " " + bodyText);
                var kept = srcNums.Count(bodyNums.Contains);
                if (kept == 0)
                {
                    issues.Add($"Kaynaktaki {srcNums.Count} sayısal ayrıntının hiçbiri metinde yok — somut bilgiler atlanmış görünüyor.");
                    critical = true;
                }
                else if (kept * 2 < srcNums.Count)
                {
                    issues.Add($"Kaynaktaki sayısal ayrıntıların azı korunmuş ({kept}/{srcNums.Count}) — eksik aktarım olabilir.");
                }
            }
        }

        if (tags is null || tags.Count == 0)
            issues.Add("Etiket yok.");

        if (string.IsNullOrWhiteSpace(shortX))
            issues.Add("Kısa (X) metni boş.");
        else if (shortX.Length > MaxShortX)
            issues.Add($"Kısa (X) metni sınırı aşıyor ({shortX.Length}>{MaxShortX}).");

        return new QualityAssessment(Passed: issues.Count == 0, Critical: critical, Issues: issues);
    }

    /// <summary>5 kelimelik kayan pencerelerin tekrar oranı. Normal haber metninde ~0'dır;
    /// aynı cümlelerin/kalıpların dönmesi oranı hızla yükseltir. Kısa metinde (&lt;120 kelime) bakılmaz.</summary>
    internal static double RepetitionRatio(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{Nd}]+").Select(m => m.Value).ToList();
        if (words.Count < 120) return 0;
        var seen = new HashSet<string>();
        var dup = 0; var total = 0;
        for (var i = 0; i + 5 <= words.Count; i++)
        {
            var shingle = string.Join(' ', words.Skip(i).Take(5));
            total++;
            if (!seen.Add(shingle)) dup++;
        }
        return total == 0 ? 0 : (double)dup / total;
    }

    /// <summary>"Kaynak metin/haber/makale ..." kalıbı YA DA aynı cümlede "kaynak" + olumsuz aktarım
    /// ("belirtilmiyor", "açıklanmamış", "bilgi verilmemiş" ...) geçen ilk cümleyi döndürür; yoksa null.</summary>
    internal static string? FindMetaSourceSentence(string text)
    {
        foreach (var raw in Regex.Split(text, @"(?<=[.!?…])\s+"))
        {
            var s = raw.Trim();
            if (s.Length < 15) continue;
            if (MetaSourceRx.IsMatch(s)) return s;
            if (SourceWordRx.IsMatch(s) && NegatedReportRx.IsMatch(s)) return s;
        }
        return null;
    }

    // "kaynak metin", "kaynak haber", "kaynak makale", "kaynak yazı(sı)"
    private static readonly Regex MetaSourceRx = new(@"kaynak\s+(metin|haber|makale|yaz[ıi])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SourceWordRx = new(@"kayna(k|ğ)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NegatedReportRx = new(
        @"belirtilm(iyor|emiş)|belirtm(iyor|emiş)|açıklanm(ıyor|amış)|açıklam(ıyor|amış)|yer\s+verilm(iyor|emiş)|bilgi\s+verilm(iyor|emiş)|detay\s+verilm(iyor|emiş)|paylaşılm(ıyor|amış)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Metindeki ≥2 haneli sayı belirteçleri (ayraçlar atılarak normalize: "1.5"/"1,5" → "15").
    /// Dil bağımsızdır — İngilizce kaynaktaki "300 billion" ile Türkçe "300 milyar" aynı belirtece düşer.</summary>
    internal static HashSet<string> NumberTokens(string text)
    {
        var set = new HashSet<string>();
        foreach (Match m in Regex.Matches(text, @"\d+(?:[.,]\d+)*"))
        {
            var v = m.Value.Replace(".", "").Replace(",", "");
            if (v.Length is >= 2 and <= 12) set.Add(v.TrimStart('0').Length == 0 ? "0" : v.TrimStart('0'));
        }
        return set;
    }

    /// <summary>Kaynak ile gövde arasında ortak, ardışık n-kelimelik (varsayılan 7) İLK diziyi bulur;
    /// yoksa null. Kelimeler küçük harfe indirilip şapkalı harfler düzleştirilir (zekâ=zeka) —
    /// böylece küçük imla farkları kopyayı gizleyemez. Kaynak farklı dildeyse doğal olarak eşleşmez.</summary>
    internal static string? FindCopiedRun(string source, string body, int n = 7)
    {
        var sw = NormalizedWords(source);
        var bw = NormalizedWords(body);
        if (sw.Count < n || bw.Count < n) return null;
        var set = new HashSet<string>();
        for (var i = 0; i + n <= sw.Count; i++) set.Add(string.Join(' ', sw.Skip(i).Take(n)));
        for (var i = 0; i + n <= bw.Count; i++)
        {
            var sh = string.Join(' ', bw.Skip(i).Take(n));
            if (set.Contains(sh)) return sh;
        }
        return null;
    }

    private static List<string> NormalizedWords(string text)
    {
        var t = text.ToLowerInvariant()
            .Replace("\u0307", "")   // 'İ'.ToLowerInvariant() artığı (i + combining dot)
            .Replace('â', 'a').Replace('î', 'i').Replace('û', 'u');
        return Regex.Matches(t, @"[\p{L}\p{Nd}]+").Select(m => m.Value).ToList();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string StripHtml(string? html) =>
        string.IsNullOrWhiteSpace(html)
            ? string.Empty
            : Regex.Replace(Regex.Replace(html, "<.*?>", " "), @"\s+", " ").Trim();
}
