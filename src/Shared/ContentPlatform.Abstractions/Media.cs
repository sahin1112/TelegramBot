namespace ContentPlatform.Abstractions;

/// <summary>Başlığı güzel formatta bir görsele basar (SkiaSharp kart). AI görsel gerekmediğinde/yedekte kullanılır.</summary>
public interface ICardRenderer
{
    /// <summary>Başlık kartını PNG bayt olarak üretir.</summary>
    byte[] RenderTitleCard(string title, string? theme, int width, int height);

    /// <summary>
    /// Bir ŞABLON görselin üzerine başlığı bindirir: en geniş boş alanı otomatik bulur, o bölgenin
    /// koyu/açıklığına göre satır satır beyaz/siyah metin seçer; kategori etiketini (sol üst) ve
    /// istenirse premium dikkat rozetini (başlığın altında ortada; kırmızı SON DAKİKA / amber ŞOK) basar.
    /// badgeText null/boş → rozet yok. PNG bayt döner. 1:1 ve 9:16 (reels/hikaye) için kullanılır.
    /// </summary>
    byte[] RenderOnTemplate(byte[] templateBytes, string title, string? badgeText, bool badgeAmber, string? category, int width, int height);
}

/// <summary>Üretilen/yüklenen görseli saklar ve erişilebilir bir URL döner (yerel/S3/CDN).</summary>
public interface IMediaStore
{
    Task<string> SaveAsync(byte[] bytes, string contentType, CancellationToken ct);
}

/// <summary>Saklanan bir görseli (URL/referans) bayt olarak okur — Telegram multipart upload için.</summary>
public interface IMediaReader
{
    Task<MediaContent?> TryReadAsync(string url, CancellationToken ct);
}
