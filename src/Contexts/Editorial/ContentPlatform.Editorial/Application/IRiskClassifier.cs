using ContentPlatform.Editorial.Domain;

namespace ContentPlatform.Editorial.Application;

/// <summary>
/// İçeriğin risk seviyesini metinden sınıflandırır (00 §10).
/// Yüksek: sağlık, siyaset, güvenlik, afet/ölüm, açık yatırım tavsiyesi → otomatik onaylanamaz.
/// Orta: ekonomi, şirket, hukuk, kripto/borsa haberi (tavsiye değil). Diğer → Düşük.
/// </summary>
public interface IRiskClassifier
{
    RiskLevel Classify(string? title, string? text);
}
