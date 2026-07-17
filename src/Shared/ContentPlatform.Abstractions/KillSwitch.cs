namespace ContentPlatform.Abstractions;

/// <summary>
/// Granular acil durdurma portu (00 §27-A). Çekirdek akışlar (üretim/yayın/keşif) bu porta bakar;
/// hatalı bir prompt yüzlerce kötü içerik/yayın üretmeden anında frenlenebilir.
/// Implementasyon Platform'da (DB'de saklanır, süreçler arası paylaşılır).
/// </summary>
public interface IKillSwitch
{
    /// <summary>AI üretimi durdurulmuş mu? (Global | Ai | Category:categoryId)</summary>
    Task<bool> IsAiStoppedAsync(Guid? categoryId, CancellationToken ct);

    /// <summary>İçe aktarma (RSS/sayfa keşfi) durdurulmuş mu? (Global | Ingestion | Category:categoryId)</summary>
    Task<bool> IsIngestionStoppedAsync(Guid? categoryId, CancellationToken ct);

    /// <summary>Bu hedefe yayın durdurulmuş mu? (Global | Publishing | Channel:platform | Account:id | Category:id)</summary>
    Task<bool> IsPublishingStoppedAsync(Platform platform, Guid? categoryId, Guid socialAccountId, CancellationToken ct);

    /// <summary>Reklam yerleşimi durdurulmuş mu? (Global | Ads)</summary>
    Task<bool> IsAdsStoppedAsync(CancellationToken ct);
}
