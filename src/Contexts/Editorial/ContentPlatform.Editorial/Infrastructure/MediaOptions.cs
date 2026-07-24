namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>appsettings "Media" bölümü. Yerel depolama; üretimde S3/CDN ile değişir.</summary>
public sealed class MediaOptions
{
    public string StoragePath { get; set; } = "wwwroot/media";

    /// <summary>Görselin dışarıdan erişilebilir tam URL öneki. Telegram sendPhoto için PUBLIC olmalı
    /// (localhost/göreli DEĞİL). Görsel gönderimi öncelikle multipart bytes ile yapılır; bu URL yalnız
    /// fallback ve web/blog gösterimi içindir.</summary>
    public string PublicBaseUrl { get; set; } = "https://hermasadabiz.com/media";

    /// <summary>Kart boyutu — Instagram/X/Telegram için ORTAK. Kare (1:1) üç platformda da kırpılmadan gösterilir.</summary>
    public int CardWidth { get; set; } = 1080;
    public int CardHeight { get; set; } = 1080;

    /// <summary>Slayt videosu (Reels/Shorts/TikTok) — DİKEY 9:16. Sayfa başına süre sn.</summary>
    public int VideoWidth { get; set; } = 1080;
    public int VideoHeight { get; set; } = 1920;
    public int VideoSlideSeconds { get; set; } = 7;

    /// <summary>ffmpeg yolu — boş/varsayılan "ffmpeg" (PATH'te aranır). Windows'ta tam yol verilebilir
    /// (ör. C:\\ffmpeg\\bin\\ffmpeg.exe). Video üretimi için sunucuda ffmpeg KURULU olmalı.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Kategori görsel şablonlarının kök klasörü (alt klasörler: 1x1, reels). Media:StoragePath
    /// gibi düşün — ÜRETİMDE Api+Worker'ın erişebileceği ORTAK bir yol olmalı (ör. C:\\Datas\\assets\\cards).
    /// Boş/göreli ise uygulama klasörüne / çalışma diznine göre çözülür (dev'de wwwroot/assets/cards bulunur).</summary>
    public string CardAssetsPath { get; set; } = "wwwroot/assets/cards";
}
