namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>appsettings "Media" bölümü. Yerel depolama; üretimde S3/CDN ile değişir.</summary>
public sealed class MediaOptions
{
    public string StoragePath { get; set; } = "wwwroot/media";
    /// <summary>Görselin dışarıdan erişilebilir tam URL öneki. Telegram sendPhoto için PUBLIC olmalı.</summary>
    public string PublicBaseUrl { get; set; } = "/media";
    public int CardWidth { get; set; } = 1200;
    public int CardHeight { get; set; } = 675;
}
