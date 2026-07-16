using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>Görseli yerel klasöre yazar/okur. Üretimde S3/CDN ile değiştirilir.</summary>
internal sealed class LocalMediaStore(IOptions<MediaOptions> options, IClock clock) : IMediaStore, IMediaReader
{
    private readonly MediaOptions _opt = options.Value;

    public async Task<string> SaveAsync(byte[] bytes, string contentType, CancellationToken ct)
    {
        Directory.CreateDirectory(_opt.StoragePath);
        var ext = Ext(contentType);
        var name = $"{clock.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}.{ext}";
        await File.WriteAllBytesAsync(Path.Combine(_opt.StoragePath, name), bytes, ct);
        return $"{_opt.PublicBaseUrl.TrimEnd('/')}/{name}";
    }

    public async Task<MediaContent?> TryReadAsync(string url, CancellationToken ct)
    {
        var name = url.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = Path.Combine(_opt.StoragePath, name);
        if (!File.Exists(path)) return null;
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var ct2 = name.EndsWith(".png") ? "image/png" : name.EndsWith(".webp") ? "image/webp" : "image/jpeg";
        return new MediaContent(bytes, ct2, name);
    }

    private static string Ext(string contentType) =>
        contentType.Contains("png") ? "png" : contentType.Contains("webp") ? "webp" : "jpg";
}
