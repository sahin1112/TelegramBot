using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>Görseli yerel klasöre yazar/okur. Üretimde S3/CDN ile değiştirilir.</summary>
internal sealed class LocalMediaStore(IOptions<MediaOptions> options, IClock clock) : IMediaStore, IMediaReader
{
    private readonly MediaOptions _opt = options.Value;

    /// <summary>
    /// Görsel klasörünün MUTLAK kökü. StoragePath göreliyse (örn. "wwwroot/media"), çalışma dizinine
    /// (CWD) göre DEĞİL, uygulamanın kurulu olduğu klasöre (AppContext.BaseDirectory) göre çözülür.
    /// Windows Servisi olarak koşan Worker'ın CWD'si System32'dir; bu yüzden göreli yol yanlış yere
    /// bakar ve görsel bulunamaz → Telegram'a URL fallback gider → "URL host is empty". Bunu kesin çözer.
    /// Mutlak StoragePath (örn. "C:\\Datas\\media") verilirse aynen kullanılır.
    /// </summary>
    private string Root => Path.GetFullPath(_opt.StoragePath, AppContext.BaseDirectory);

    public async Task<string> SaveAsync(byte[] bytes, string contentType, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var ext = Ext(contentType);
        var name = $"{clock.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}.{ext}";
        await File.WriteAllBytesAsync(Path.Combine(Root, name), bytes, ct);
        return $"{_opt.PublicBaseUrl.TrimEnd('/')}/{name}";
    }

    public async Task<MediaContent?> TryReadAsync(string url, CancellationToken ct)
    {
        // URL ya da yolun yalnızca dosya adını al (query string'i de temizle): ".../abc.png?x=1" → "abc.png"
        var name = url.Split('/').LastOrDefault()?.Split('?', '#').FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return null;
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var ct2 = name.EndsWith(".png") ? "image/png" : name.EndsWith(".webp") ? "image/webp" : name.EndsWith(".mp3") ? "audio/mpeg" : name.EndsWith(".mp4") ? "video/mp4" : "image/jpeg";
        return new MediaContent(bytes, ct2, name);
    }

    private static string Ext(string contentType) =>
        contentType.Contains("mp4") ? "mp4" : contentType.Contains("mpeg") || contentType.Contains("mp3") ? "mp3" : contentType.Contains("png") ? "png" : contentType.Contains("webp") ? "webp" : "jpg";
}
