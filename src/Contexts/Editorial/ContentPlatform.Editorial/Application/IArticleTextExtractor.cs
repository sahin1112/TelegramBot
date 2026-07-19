namespace ContentPlatform.Editorial.Application;

/// <summary>
/// Kaynak URL'den OKUNUR makale metnini çıkarır (site iskeleti/kaynak kodu değil).
/// AI üretimine kaliteli girdi sağlar; başarısızlıkta null döner (çağıran RSS özetine düşer).
/// </summary>
public interface IArticleTextExtractor
{
    Task<string?> ExtractAsync(string url, CancellationToken ct);
}
