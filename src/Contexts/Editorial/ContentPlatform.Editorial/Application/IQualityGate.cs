namespace ContentPlatform.Editorial.Application;

/// <summary>
/// Üretilen içeriğin yayından önce kontrol kapısı (00 §10, §27-B).
/// Kritik sorun varsa içerik OTOMATİK yayınlanmaz; incelemeye alınır (oto-yeniden-üretime değil, insana).
/// sourceText verilirse kaynak-sadakat kontrolleri de yapılır (sayısal ayrıntıların korunması).
/// </summary>
public interface IQualityGate
{
    QualityAssessment Evaluate(string? title, string? shortX, string? bodyHtml, IReadOnlyList<string> tags, string? sourceText = null);
}

/// <summary>Kalite değerlendirmesi. Critical=true ise içerik tutulur (yayınlanmaz).</summary>
public sealed record QualityAssessment(bool Passed, bool Critical, IReadOnlyList<string> Issues);
