using ContentPlatform.SharedKernel;

namespace ContentPlatform.Editorial.Domain;

public enum MediaKind { AiImage, SkiaCard, Manual }

/// <summary>Bir içeriğin görseli. webp/png, düşük çözünürlük (maliyet/CDN).</summary>
public sealed class MediaAsset : Entity
{
    private MediaAsset() { }

    public MediaAsset(Guid contentItemId, MediaKind kind, string url, int width, int height, bool titleBurned, IClock clock)
    {
        ContentItemId = contentItemId;
        Kind = kind;
        Url = url;
        Width = width;
        Height = height;
        TitleBurned = titleBurned;
        CreatedAt = clock.UtcNow;
    }

    public Guid ContentItemId { get; private set; }
    public MediaKind Kind { get; private set; }
    public string Url { get; private set; } = default!;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool TitleBurned { get; private set; }
}
