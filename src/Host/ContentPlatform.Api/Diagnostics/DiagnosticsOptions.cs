namespace ContentPlatform.Api.Diagnostics;

/// <summary>
/// Uzaktan log okuma ayarları. /_diag/logs ucu bu ayarlarla korunur.
/// Ziyaretçiler bu ucu göremez; yalnızca gizli anahtarı (LogFeedKey) bilen erişebilir.
/// </summary>
public sealed class DiagnosticsOptions
{
    /// <summary>Log akışını tümden açar/kapatır. Kapalıyken uç 404 döner.</summary>
    public bool EnableLogFeed { get; set; } = true;

    /// <summary>
    /// Gizli anahtar. /_diag/logs?key=... bununla eşleşmezse uç 404 döner (varlığını sızdırmaz).
    /// Boşsa akış kapalıdır. Sunucuda ortam değişkeni ile de verilebilir: Diagnostics__LogFeedKey
    /// </summary>
    public string LogFeedKey { get; set; } = "";

    /// <summary>
    /// Api VE Worker'ın JSON (.clef) loglarını yazdığı ORTAK klasör.
    /// İki süreç de AYNI klasörü göstermeli ki uç ikisini birleştirebilsin.
    /// </summary>
    public string LogDirectory { get; set; } = @"C:\Datas\logs";

    /// <summary>Tek istekte en fazla kaç dakikalık log verilebilir (üst sınır).</summary>
    public int FeedMaxMinutes { get; set; } = 120;
}
