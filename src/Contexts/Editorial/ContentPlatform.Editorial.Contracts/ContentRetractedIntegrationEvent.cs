using ContentPlatform.Abstractions;

namespace ContentPlatform.Editorial.Contracts;

/// <summary>
/// İçerik geri çekildi/silindi (arşivlendi). Site gönderiyi public blogdan kaldırır;
/// Publishing planlı/başarısız (henüz gönderilmemiş) yayınları iptal eder. Gönderilmiş sosyal
/// paylaşımlara dokunulmaz (platform API'leri geri almayı desteklemez).
/// </summary>
public sealed record ContentRetractedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ContentItemId) : IIntegrationEvent;
