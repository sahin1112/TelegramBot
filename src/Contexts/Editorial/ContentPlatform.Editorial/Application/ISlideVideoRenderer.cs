namespace ContentPlatform.Editorial.Application;

/// <summary>Reels/Shorts/TikTok için dikey slayt videosu üretir (cümle bütünlüklü sayfalar × 7 sn, mp4).</summary>
public interface ISlideVideoRenderer
{
    /// <summary>musicBytes: opsiyonel arka plan müziği (mp3) — videoya gömülür (süreye kesilir + fade-out).
    /// style: 0..19 şablon numarası (hepsi koyu tema, farklı renk/dekor); null = RASTGELE şablon.
    /// category: içerik kategorisi adı — slaytlara rozet olarak basılır (ne haberi olduğu anlaşılsın).
    /// backgroundImage: opsiyonel AI arka plan görseli (dikey) — verilirse şablon zemini yerine
    /// cover-crop edilip üstüne koyu katman (scrim) basılır; yazılar onun üzerine çizilir.</summary>
    Task<byte[]> RenderSlidesVideoAsync(string title, string text, byte[]? musicBytes, int? style, string? category, byte[]? backgroundImage, string? badgeText, bool badgeAmber, CancellationToken ct);
}
