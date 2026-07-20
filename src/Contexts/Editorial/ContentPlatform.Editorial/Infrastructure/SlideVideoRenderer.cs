using System.Diagnostics;
using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Reels / YouTube Shorts / TikTok için DİKEY (1080x1920) slayt videosu üretir:
/// X metni CÜMLE BÜTÜNLÜĞÜ korunarak sayfalara bölünür — sayfa sayısı metne göre esner
/// (kısa metin 1-2 sayfada biter, uzun metin 6 sayfaya kadar çıkar; sayfa başına 7 sn).
/// Hiçbir cümle sayfa ortasında KOPMAZ; aşırı uzun cümle önce virgülden bölünür.
/// Kareler SkiaSharp ile çizilir, ffmpeg ile birleştirilir (komut satırı bire bir test edildi).
/// ffmpeg yolu Media:FfmpegPath (varsayılan "ffmpeg" — PATH'te olmalı; Windows'ta ffmpeg.exe kur).
/// </summary>
internal sealed class SlideVideoRenderer(
    IOptions<MediaOptions> mediaOptions,
    IOptions<SiteOptions> siteOptions,
    ILogger<SlideVideoRenderer> logger) : ISlideVideoRenderer
{
    private static readonly SKColor TextC = new(0xF4, 0xF7, 0xFC);
    private static readonly SKColor MutedC = new(0x8A, 0x95, 0xA8);

    /// <summary>Bir video şablonu: zemin gradyanı + vurgu rengi + dekor türü. HEPSİ KOYU tema
    /// (profil karanlık); renk ve dekor değişir ki art arda Reels'ler tekdüze görünmesin.</summary>
    private sealed record VideoStyle(string Name, SKColor Bg1, SKColor Bg2, SKColor Accent, SKColor Accent2, SKColor TitleC, int Deco);
    // Deco: 0=radyal ışıma · 1=ince ızgara · 2=çapraz şerit · 3=büyük halka · 4=köşe çerçevesi
    private static readonly VideoStyle[] Styles =
    {
        new("Turuncu",  new(0x0B,0x0B,0x0F), new(0x14,0x14,0x1A), new(0xE8,0x55,0x2E), new(0xF2,0x8D,0x21), new(0xC9,0xD2,0xE0), 0),
        new("Kızıl",    new(0x12,0x0A,0x0A), new(0x1E,0x10,0x10), new(0xE5,0x3E,0x3E), new(0xFF,0x7A,0x5C), new(0xE0,0xC9,0xC9), 1),
        new("Altın",    new(0x0F,0x0D,0x07), new(0x1A,0x16,0x0C), new(0xD9,0xA4,0x2A), new(0xF2,0xC5,0x5C), new(0xE2,0xD8,0xC0), 2),
        new("Zümrüt",   new(0x07,0x10,0x0C), new(0x0D,0x1A,0x14), new(0x2E,0xB8,0x72), new(0x6C,0xE5,0xA8), new(0xC2,0xDE,0xCE), 3),
        new("Turkuaz",  new(0x06,0x10,0x12), new(0x0C,0x1A,0x1E), new(0x1F,0xB8,0xC4), new(0x5C,0xE5,0xE0), new(0xC0,0xDC,0xE0), 4),
        new("Okyanus",  new(0x07,0x0C,0x14), new(0x0D,0x15,0x22), new(0x2E,0x7C,0xE8), new(0x5C,0xB0,0xF2), new(0xC5,0xD3,0xE8), 0),
        new("İndigo",   new(0x0A,0x0A,0x16), new(0x12,0x12,0x26), new(0x5C,0x5C,0xE8), new(0x8F,0x8A,0xFF), new(0xCC,0xCC,0xE8), 1),
        new("Mor",      new(0x0E,0x08,0x14), new(0x18,0x0F,0x22), new(0x9E,0x4C,0xE8), new(0xC0,0x7A,0xFF), new(0xD8,0xC8,0xE8), 2),
        new("Magenta",  new(0x12,0x07,0x10), new(0x1E,0x0D,0x1A), new(0xE0,0x2E,0x9E), new(0xFF,0x6C,0xC4), new(0xE8,0xC5,0xDC), 3),
        new("Pembe",    new(0x14,0x0A,0x0D), new(0x22,0x11,0x16), new(0xF2,0x5C,0x7A), new(0xFF,0x9E,0xAE), new(0xE8,0xCC,0xD2), 4),
        new("Lime",     new(0x0C,0x10,0x07), new(0x14,0x1A,0x0C), new(0x8F,0xC4,0x2E), new(0xC4,0xE8,0x5C), new(0xD6,0xE0,0xC2), 0),
        new("Buz",      new(0x0A,0x0E,0x12), new(0x10,0x18,0x1E), new(0x7A,0xC4,0xE8), new(0xB0,0xE0,0xF7), new(0xD0,0xDE,0xE6), 1),
        new("Bakır",    new(0x10,0x0B,0x08), new(0x1C,0x13,0x0E), new(0xC4,0x6C,0x3E), new(0xE8,0x9E,0x6C), new(0xE0,0xD0,0xC6), 2),
        new("Çelik",    new(0x0C,0x0E,0x11), new(0x14,0x18,0x1E), new(0x8A,0x9C,0xB8), new(0xB8,0xC8,0xDE), new(0xD5,0xDC,0xE5), 3),
        new("GeceMoru", new(0x0B,0x08,0x18), new(0x14,0x0E,0x28), new(0x7A,0x3E,0xE8), new(0xB0,0x7A,0xFF), new(0xD2,0xC8,0xE8), 4),
        new("NeonYeşil",new(0x06,0x0F,0x0A), new(0x0A,0x18,0x10), new(0x2E,0xE8,0x7A), new(0x7A,0xFF,0xB0), new(0xC2,0xE2,0xD0), 1),
        new("Cyan",     new(0x05,0x0F,0x14), new(0x0A,0x18,0x20), new(0x2E,0xD0,0xE8), new(0x7A,0xF2,0xFF), new(0xC2,0xDE,0xE4), 2),
        new("Amber",    new(0x12,0x0E,0x06), new(0x1E,0x17,0x0A), new(0xE8,0x9E,0x2E), new(0xFF,0xC4,0x5C), new(0xE6,0xDA,0xC4), 3),
        new("Bordo",    new(0x14,0x08,0x0B), new(0x20,0x0E,0x13), new(0xB8,0x2E,0x4A), new(0xE8,0x5C,0x7A), new(0xE0,0xC6,0xCC), 4),
        new("Petrol",   new(0x06,0x0E,0x0E), new(0x0B,0x17,0x17), new(0x2E,0x9E,0x9E), new(0x5C,0xD0,0xC4), new(0xC2,0xDA,0xDA), 0),
    };

    public async Task<byte[]> RenderSlidesVideoAsync(string title, string text, byte[]? musicBytes, int? style, string? category, byte[]? backgroundImage, CancellationToken ct)
    {
        var o = mediaOptions.Value;
        int w = o.VideoWidth > 0 ? o.VideoWidth : 1080;
        int h = o.VideoHeight > 0 ? o.VideoHeight : 1920;
        int secs = o.VideoSlideSeconds > 0 ? o.VideoSlideSeconds : 7;

        // Şablon seçimi: geçerli indeks verildiyse o; yoksa RASTGELE (her video farklı görünsün).
        var s = style is { } si && si >= 0 && si < Styles.Length ? Styles[si] : Styles[Random.Shared.Next(Styles.Length)];
        logger.LogInformation("Video şablonu: {Name}", s.Name);

        // AI arka plan görseli (opsiyonel): bir kez çözülür, tüm sayfalarda cover-crop kullanılır.
        using var bgImage = backgroundImage is { Length: > 0 } ? SKImage.FromEncodedData(backgroundImage) : null;
        if (backgroundImage is { Length: > 0 } && bgImage is null)
            logger.LogWarning("Video arka plan görseli çözülemedi — şablon zeminiyle devam ediliyor.");

        var parts = SplitSlides(text);
        var dir = Path.Combine(Path.GetTempPath(), "cp-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // ÇOK SAYFALI videoda sayfa süresi köşedeki ERİYEN PASTA ile hissettirilir: sayfa tek
            // durağan kare yerine 0.5 sn'lik adımlarla çizilir, pasta her adımda biraz azalır.
            // Tek sayfalık videoda pasta yok (gerek yok) → tek kare, eski davranış.
            var frames = new List<(string File, double Secs)>();
            var fi = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                var steps = parts.Count > 1 ? Math.Max(1, secs * 2) : 1;
                for (var k = 0; k < steps; k++)
                {
                    var remaining = parts.Count > 1 ? 1f - (k + 0.5f) / steps : -1f;
                    var png = DrawSlide(title, parts[i], i, parts.Count, w, h, s, category, bgImage, remaining);
                    var file = Path.Combine(dir, $"f{fi++}.png");
                    await File.WriteAllBytesAsync(file, png, ct);
                    frames.Add((file, (double)secs / steps));
                }
            }
            string? musicPath = null;
            if (musicBytes is { Length: > 0 })
            {
                musicPath = Path.Combine(dir, "music.mp3");
                await File.WriteAllBytesAsync(musicPath, musicBytes, ct);
            }
            var outPath = Path.Combine(dir, "out.mp4");
            await RunFfmpegAsync(frames, outPath, musicPath, ct);
            return await File.ReadAllBytesAsync(outPath, ct);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* geçici klasör */ }
        }
    }

    // ---------- metni CÜMLE BÜTÜNLÜĞÜYLE sayfalara böl (sayfa sayısı metne göre esner) ----------
    // Kurallar:
    //  * Sayfa sınırı YALNIZ cümle sonuna (. ! ? : …) gelir — cümle asla ortadan kopmaz.
    //  * Tek cümle bir sayfaya sığmayacak kadar uzunsa önce VİRGÜL/noktalı virgülden bölünür;
    //    o da yetmezse (virgülsüz dev cümle) zorunlu kelime kesimi yapılır (son çare).
    //  * Sayfa sayısı = metin uzunluğuna göre 1..6 (6 × 7 sn = 42 sn tavan). Metin 3 sayfayı
    //    doldurmuyorsa 2 sayfada (hatta 1'de) biter; uzunsa sayfa eklenir.
    internal static List<string> SplitSlides(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return new List<string> { "" };

        const int TargetPerSlide = 200; // rahat okunan sayfa uzunluğu (~5-6 satır)
        const int HardMax = 300;        // tek sayfada üst sınır — bunu aşan cümle virgülden bölünür
        const int MaxSlides = 6;

        // 1) Cümlelere ayır (cümle sonu noktalaması taşıyan kelimede kes).
        var sentences = new List<string>();
        var cur = "";
        foreach (var wd in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            cur = cur.Length == 0 ? wd : cur + " " + wd;
            if (wd.EndsWith('.') || wd.EndsWith('!') || wd.EndsWith('?') || wd.EndsWith(':') || wd.EndsWith('…'))
            { sentences.Add(cur); cur = ""; }
        }
        if (cur.Length > 0) sentences.Add(cur);

        // 2) Sayfaya sığmayacak uzunluktaki cümleyi anlam sınırında (virgül/;) parçala.
        var units = new List<string>();
        foreach (var s in sentences)
        {
            if (s.Length <= HardMax) { units.Add(s); continue; }
            var clause = "";
            foreach (var wd in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var cand = clause.Length == 0 ? wd : clause + " " + wd;
                if (clause.Length > 0 && cand.Length > HardMax) // virgülsüz dev cümle: zorunlu kesim
                { units.Add(clause); clause = wd; continue; }
                clause = cand;
                if ((wd.EndsWith(',') || wd.EndsWith(';')) && clause.Length >= TargetPerSlide * 0.6)
                { units.Add(clause); clause = ""; }
            }
            if (clause.Length > 0) units.Add(clause);
        }

        // 3) Sayfa sayısını metne göre seç (1..6; birim sayısını aşamaz).
        var total = units.Sum(u => u.Length + 1);
        var n = Math.Clamp((int)Math.Ceiling(total / (double)TargetPerSlide), 1, MaxSlides);
        n = Math.Min(n, units.Count);
        if (n == 1 && units.Count >= 2 && total > 140) n = 2; // tek tıkış sayfa yerine 2 ferah sayfa
        if (n <= 1) return new List<string> { string.Join(" ", units) };

        // 4) Birimleri (tam cümleler) ~dengeli paketle; sonraki her sayfaya en az 1 birim kalsın.
        var slides = new List<string>();
        var idx = 0;
        for (var sIdx = 0; sIdx < n && idx < units.Count; sIdx++)
        {
            if (sIdx == n - 1) { slides.Add(string.Join(" ", units.Skip(idx))); idx = units.Count; break; }
            var remSlides = n - sIdx;
            var remLen = units.Skip(idx).Sum(u => u.Length + 1);
            var target = remLen / (double)remSlides;
            var acc = units[idx++];
            while (idx < units.Count && units.Count - idx >= remSlides)
            {
                var cand = acc + " " + units[idx];
                if (cand.Length > HardMax) break;
                if (cand.Length > target * 1.25 && acc.Length >= target * 0.6) break;
                acc = cand; idx++;
            }
            slides.Add(acc);
        }
        return slides;
    }

    // ---------- tek slayt (1080x1920) ----------
    private byte[] DrawSlide(string title, string part, int index, int total, int w, int h, VideoStyle st, string? category, SKImage? bgImage, float remaining)
    {
        var s = w / 1080f;
        var (Accent, Accent2, TitleC) = (st.Accent, st.Accent2, st.TitleC);
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var canvas = surface.Canvas;

        if (bgImage is not null)
        {
            // AI ARKA PLAN: cover-crop + sayfadan sayfaya hafif zoom (monotonluğu kırar; sayfa içinde sabit).
            var zoom = 1f + 0.045f * index;
            var scale = Math.Max((float)w / bgImage.Width, (float)h / bgImage.Height) * zoom;
            var dw = bgImage.Width * scale; var dh = bgImage.Height * scale;
            var dst = new SKRect((w - dw) / 2f, (h - dh) / 2f, (w + dw) / 2f, (h + dh) / 2f);
            using (var bp = new SKPaint { IsAntialias = true })
                canvas.DrawImage(bgImage, dst, new SKSamplingOptions(SKCubicResampler.Mitchell), bp);
            // SCRIM: yazı okunabilirliği için koyu katman — üst (rozetler) ve alt (metin/altbilgi) daha koyu.
            using var scrim = new SKPaint { IsAntialias = true };
            scrim.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h),
                new[] { new SKColor(0, 0, 0, 0xB4), new SKColor(0, 0, 0, 0x82), new SKColor(0, 0, 0, 0xC6) },
                new[] { 0f, 0.42f, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawRect(SKRect.Create(0, 0, w, h), scrim);
        }
        else
        {
        // Zemin
        using (var bg = new SKPaint { IsAntialias = true })
        {
            bg.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
                new[] { st.Bg1, st.Bg2, st.Bg1 }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawRect(SKRect.Create(0, 0, w, h), bg);
        }
        // Dekor (şablona göre) — hepsi hafif, metni ezmez (AI arka planda dekor çizilmez)
        switch (st.Deco)
        {
            case 0: // radyal ışıma
                using (var glow = new SKPaint { IsAntialias = true })
                {
                    glow.Shader = SKShader.CreateRadialGradient(new SKPoint(w * 0.85f, h * 0.08f), h * 0.5f,
                        new[] { Accent.WithAlpha(0x30), Accent.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
                    canvas.DrawRect(SKRect.Create(0, 0, w, h), glow);
                }
                break;
            case 1: // ince ızgara
                using (var grid = new SKPaint { IsAntialias = true, Color = Accent.WithAlpha(0x14), StrokeWidth = 1.5f * s, Style = SKPaintStyle.Stroke })
                {
                    for (var gx = 0f; gx <= w; gx += 120f * s) canvas.DrawLine(gx, 0, gx, h, grid);
                    for (var gy = 0f; gy <= h; gy += 120f * s) canvas.DrawLine(0, gy, w, gy, grid);
                }
                break;
            case 2: // çapraz şerit (üst bölge)
                using (var stripe = new SKPaint { IsAntialias = true })
                {
                    stripe.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, 0),
                        new[] { Accent.WithAlpha(0x22), Accent2.WithAlpha(0x0E) }, null, SKShaderTileMode.Clamp);
                    using var path = new SKPath();
                    path.MoveTo(0, h * 0.10f); path.LineTo(w, h * 0.02f);
                    path.LineTo(w, h * 0.14f); path.LineTo(0, h * 0.24f); path.Close();
                    canvas.DrawPath(path, stripe);
                }
                break;
            case 3: // büyük halka (sağ üst)
                using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 26f * s, Color = Accent.WithAlpha(0x26) })
                    canvas.DrawCircle(w * 0.92f, h * 0.10f, 240f * s, ring);
                using (var ring2 = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 10f * s, Color = Accent2.WithAlpha(0x1E) })
                    canvas.DrawCircle(w * 0.92f, h * 0.10f, 320f * s, ring2);
                break;
            case 4: // köşe çerçeveleri
                using (var corner = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8f * s, Color = Accent.WithAlpha(0x55) })
                {
                    var m2 = 56f * s; var len = 200f * s;
                    canvas.DrawLine(m2, m2 + len, m2, m2, corner); canvas.DrawLine(m2, m2, m2 + len, m2, corner);
                    canvas.DrawLine(w - m2 - len, h - m2, w - m2, h - m2, corner); canvas.DrawLine(w - m2, h - m2, w - m2, h - m2 - len, corner);
                }
                break;
        }
        } // else (şablon zemini) sonu
        // Üst aksan çizgisi
        using (var bar = new SKPaint { IsAntialias = true })
        {
            bar.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, 0),
                new[] { Accent, Accent2 }, null, SKShaderTileMode.Clamp);
            canvas.DrawRect(SKRect.Create(0, 0, w, 10f * s), bar);
        }

        var margin = 84f * s;

        // Marka rozeti + KATEGORİ rozeti (yan yana; kategori dolu zeminli — ne haberi olduğu ilk bakışta anlaşılır)
        var pillX = margin;
        var brand = BrandText();
        if (brand.Length > 0)
        {
            using var bf = MakeFont(34f * s, SKFontStyleWeight.Bold);
            var tw = bf.MeasureText(brand);
            var pillH = 72f * s; var padX = 30f * s;
            var rect = new SKRect(pillX, 120f * s, pillX + tw + padX * 2, 120f * s + pillH);
            using (var fill = new SKPaint { IsAntialias = true, Color = Accent.WithAlpha(0x26) })
                canvas.DrawRoundRect(rect, pillH / 2, pillH / 2, fill);
            using (var stroke = new SKPaint { IsAntialias = true, Color = Accent.WithAlpha(0x90), Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f * s })
                canvas.DrawRoundRect(rect, pillH / 2, pillH / 2, stroke);
            using var bp = new SKPaint { IsAntialias = true, Color = Accent2 };
            canvas.DrawText(brand, rect.Left + padX, rect.MidY + bf.Size * 0.36f, bf, bp);
            pillX = rect.Right + 16f * s;
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category!.Trim().ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
            using var cf = MakeFont(34f * s, SKFontStyleWeight.Bold);
            var ctw = cf.MeasureText(cat);
            var pillH = 72f * s; var padX = 30f * s;
            // Marka rozetinin yanına sığmazsa kendi başına marjdan başlar (taşma olmaz).
            if (pillX + ctw + padX * 2 > w - margin) pillX = margin;
            var rect = new SKRect(pillX, brand.Length > 0 && pillX == margin ? 208f * s : 120f * s,
                pillX + ctw + padX * 2, (brand.Length > 0 && pillX == margin ? 208f : 120f) * s + pillH);
            using (var fill = new SKPaint { IsAntialias = true })
            {
                fill.Shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, 0), new SKPoint(rect.Right, 0),
                    new[] { Accent, Accent2 }, null, SKShaderTileMode.Clamp);
                canvas.DrawRoundRect(rect, pillH / 2, pillH / 2, fill);
            }
            using var cp = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0xFF, 0xFF, 0xF2) };
            canvas.DrawText(cat, rect.Left + padX, rect.MidY + cf.Size * 0.36f, cf, cp);
        }

        // ERİYEN PASTA (sayfa süresi göstergesi): çok sayfalı videoda sağ üstte, küçük.
        // remaining 1→0 eridikçe kalan dilim azalır; izleyici sayfanın biteceğini hisseder.
        if (remaining >= 0f && total > 1)
        {
            var pr = 34f * s;
            var pcx = w - margin - pr; var pcy = 120f * s + 36f * s;
            using (var pring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5f * s, Color = MutedC.WithAlpha(0x55) })
                canvas.DrawCircle(pcx, pcy, pr, pring);
            var sweep = Math.Min(359.5f, 360f * remaining);
            if (sweep > 0.5f)
            {
                using var pie = new SKPaint { IsAntialias = true, Color = Accent.WithAlpha(0xCC) };
                using var ppath = new SKPath();
                ppath.MoveTo(pcx, pcy);
                ppath.ArcTo(new SKRect(pcx - pr + 5f * s, pcy - pr + 5f * s, pcx + pr - 5f * s, pcy + pr - 5f * s), -90f, sweep, false);
                ppath.Close();
                canvas.DrawPath(ppath, pie);
            }
        }

        // Başlık (bağlam — her sayfada, küçük)
        var maxWidth = w - margin * 2;
        using (var tf = MakeFont(44f * s, SKFontStyleWeight.Bold))
        {
            var tLines = WrapText(title, tf, maxWidth);
            if (tLines.Count > 3) { tLines = tLines.Take(3).ToList(); tLines[^1] = Ellipsize(tLines[^1], tf, maxWidth); }
            using var tp = new SKPaint { IsAntialias = true, Color = TitleC };
            var ty = 300f * s;
            foreach (var line in tLines) { canvas.DrawText(line, margin, ty, tf, tp); ty += tf.Size * 1.25f; }
        }

        // Ana metin parçası (büyük, dikeyde ortalı)
        var areaTop = 520f * s; var areaBottom = h - 420f * s;
        List<string> lines = new(); SKFont? font = null; float lineH = 0;
        foreach (var size in new[] { 64f, 58f, 52f, 47f, 42f, 38f })
        {
            font?.Dispose();
            font = MakeFont(size * s, SKFontStyleWeight.SemiBold);
            lines = WrapText(part, font, maxWidth);
            lineH = font.Size * 1.32f;
            if (lines.Count * lineH <= areaBottom - areaTop) break;
        }
        var maxLines = Math.Max(1, (int)((areaBottom - areaTop) / lineH));
        if (lines.Count > maxLines) { lines = lines.Take(maxLines).ToList(); lines[^1] = Ellipsize(lines[^1], font!, maxWidth); }
        var blockH = lines.Count * lineH;
        var top = areaTop + (areaBottom - areaTop - blockH) / 2;
        // Sol aksan çubuğu
        using (var vb = new SKPaint { IsAntialias = true })
        {
            vb.Shader = SKShader.CreateLinearGradient(new SKPoint(0, top), new SKPoint(0, top + blockH),
                new[] { Accent, Accent2 }, null, SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SKRect(margin - 34f * s, top + 8f * s, margin - 24f * s, top + blockH - 4f * s), 5f * s, 5f * s, vb);
        }
        using (var mp = new SKPaint { IsAntialias = true, Color = TextC })
        {
            var y = top + font!.Size;
            foreach (var line in lines) { canvas.DrawText(line, margin, y, font, mp); y += lineH; }
        }
        font!.Dispose();

        // Sayfa göstergesi (noktalar + 1/3)
        var dotY = h - 300f * s; var dotR = 10f * s; var gap = 40f * s;
        var startX = w / 2f - (total - 1) * gap / 2f;
        for (var i = 0; i < total; i++)
        {
            using var dp = new SKPaint { IsAntialias = true, Color = i == index ? Accent : MutedC.WithAlpha(0x60) };
            canvas.DrawCircle(startX + i * gap, dotY, i == index ? dotR * 1.25f : dotR, dp);
        }
        using (var pf = MakeFont(30f * s, SKFontStyleWeight.Medium))
        using (var pp = new SKPaint { IsAntialias = true, Color = MutedC })
        {
            var pTxt = $"{index + 1}/{total}";
            canvas.DrawText(pTxt, w / 2f - pf.MeasureText(pTxt) / 2f, dotY + 64f * s, pf, pp);
        }

        // Alt bilgi (alan adı)
        var domain = Host();
        if (domain.Length > 0)
        {
            using var ff = MakeFont(36f * s, SKFontStyleWeight.Bold);
            using var fp = new SKPaint { IsAntialias = true, Color = TextC.WithAlpha(0xD9) };
            using (var dp2 = new SKPaint { IsAntialias = true, Color = Accent })
                canvas.DrawCircle(margin + 8f * s, h - 140f * s - 12f * s, 9f * s, dp2);
            canvas.DrawText(domain, margin + 34f * s, h - 140f * s, ff, fp);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 92);
        return data.ToArray();
    }

    // ---------- ffmpeg (test edilmiş komut) ----------
    private async Task RunFfmpegAsync(List<(string File, double Secs)> frames, string outPath, string? musicPath, CancellationToken ct)
    {
        var ffmpeg = string.IsNullOrWhiteSpace(mediaOptions.Value.FfmpegPath) ? "ffmpeg" : mediaOptions.Value.FfmpegPath;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var inputs = string.Concat(frames.Select(f => $"-loop 1 -t {f.Secs.ToString("0.###", ci)} -i \"{f.File}\" "));
        var streams = string.Concat(Enumerable.Range(0, frames.Count).Select(i => $"[{i}:v]"));
        var total = frames.Sum(f => f.Secs);
        var totalS = total.ToString("0.###", ci);
        string args;
        if (musicPath is not null)
        {
            // MÜZİKLİ: ses videoya gömülür — toplam süreye kesilir, son 1.5 sn fade-out (komut test edildi).
            var fadeStart = Math.Max(0, total - 1.5).ToString("0.###", ci);
            args = $"-y -loglevel error {inputs}-i \"{musicPath}\" " +
                   $"-filter_complex \"{streams}concat=n={frames.Count}:v=1:a=0,fps=30,format=yuv420p[v];[{frames.Count}:a]atrim=0:{totalS},afade=t=out:st={fadeStart}:d=1.5[a]\" " +
                   $"-map \"[v]\" -map \"[a]\" -c:v libx264 -preset veryfast -c:a aac -b:a 128k -movflags +faststart \"{outPath}\"";
        }
        else
        {
            args = $"-y -loglevel error {inputs}-filter_complex \"{streams}concat=n={frames.Count}:v=1:a=0,fps=30,format=yuv420p\" -c:v libx264 -preset veryfast -movflags +faststart \"{outPath}\"";
        }

        var psi = new ProcessStartInfo(ffmpeg, args)
        { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("ffmpeg başlatılamadı.");
        var err = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0 || !File.Exists(outPath))
        {
            logger.LogWarning("ffmpeg hata ({Code}): {Err}", proc.ExitCode, err);
            throw new InvalidOperationException(
                "Video kodlanamadı. Sunucuda ffmpeg kurulu mu? (Media:FfmpegPath ile yol verilebilir.) Detay: " +
                (err.Length > 300 ? err[..300] : err));
        }
    }

    // ---------- yardımcılar ----------
    private string BrandText()
    {
        var host = Host();
        if (host.Length > 0) return host.ToUpperInvariant();
        var name = siteOptions.Value.SiteName;
        return string.IsNullOrWhiteSpace(name) ? "" : name.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
    }

    private string Host()
    {
        var baseUrl = siteOptions.Value.BaseUrlTrimmed;
        if (baseUrl.Length == 0) return "";
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) ? u.Host : "";
    }

    private static SKFont MakeFont(float size, SKFontStyleWeight weight)
    {
        var typeface =
            SKTypeface.FromFamilyName("Segoe UI", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Arial", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
        return new SKFont(typeface, size) { Subpixel = true };
    }

    private static string Ellipsize(string line, SKFont font, float maxWidth)
    {
        const string dots = "…";
        if (font.MeasureText(line + dots) <= maxWidth) return line + dots;
        while (line.Length > 1 && font.MeasureText(line + dots) > maxWidth)
            line = line[..^1].TrimEnd();
        return line + dots;
    }

    private static List<string> WrapText(string text, SKFont font, float maxWidth)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) > maxWidth && current.Length > 0)
            { lines.Add(current); current = word; }
            else current = candidate;
        }
        if (current.Length > 0) lines.Add(current);
        return lines.Count > 0 ? lines : new List<string> { text ?? "" };
    }
}
