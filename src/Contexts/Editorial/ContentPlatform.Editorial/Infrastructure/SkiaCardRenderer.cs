using ContentPlatform.Abstractions;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Premium başlık kartı (SkiaSharp 3.x — SKFont API). Instagram / X / Telegram için ortak boyut
/// (varsayılan 1080x1080 kare) üretir; tüm ölçüler orana göre ölçeklenir, dikey/yatay da çalışır.
///
/// 24+ ŞABLON: koyu ve açık temalar, farklı aksan renkleri ve zemin dokuları (nokta, ızgara,
/// çapraz çizgi, halftone, sade, grafik). `theme` parametresi şablonu seçer:
///  - sayı ("0".."23") → o şablon,
///  - boş/null/"auto" → başlığa göre DETERMİNİSTİK seçim (her haber farklı görünür, aynı haber stabil).
/// </summary>
internal sealed class SkiaCardRenderer(IOptions<SiteOptions> siteOptions, IClock clock) : ICardRenderer
{
    private static SKColor C(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Bir kart şablonu: tema (açık/koyu), zemin gradyanı, aksan renkleri, zemin dokusu.</summary>
    private sealed record Style(bool Light, uint Bg1, uint Bg2, uint Bg3, uint Accent, uint Accent2, int Pattern);

    // Pattern: 0 nokta · 1 ızgara · 2 çapraz çizgi · 3 halftone (köşe) · 4 sade (ışıma) · 5 grafik (yükselen çizgi + barlar)
    private static readonly Style[] Styles =
    {
        // ---- KOYU ----
        new(false, 0x0A0F1E, 0x101A2E, 0x0D2530, 0x2EC5B6, 0x3A86FF, 0),  // 0  teal/mavi nokta (klasik)
        new(false, 0x0B0B0F, 0x141418, 0x0E0E12, 0xFF7A18, 0xF26D21, 5),  // 1  turuncu grafik (kripto)
        new(false, 0x0A0A0C, 0x17120E, 0x0C0C0E, 0xF26D21, 0xE23B2E, 2),  // 2  turuncu/kırmızı çapraz
        new(false, 0x0B0E14, 0x121826, 0x0A0F16, 0x3A86FF, 0x22D3EE, 1),  // 3  mavi ızgara
        new(false, 0x0C0A12, 0x171226, 0x0B0910, 0x7C5CFF, 0x8B5CF6, 3),  // 4  mor halftone
        new(false, 0x081410, 0x0E1A12, 0x071009, 0x22C55E, 0x14B8A6, 0),  // 5  yeşil nokta
        new(false, 0x100A0A, 0x1A1010, 0x0D0808, 0xE23B2E, 0xFF7A18, 5),  // 6  kırmızı grafik (son dakika)
        new(false, 0x0A0C10, 0x121620, 0x0A0D12, 0xE6B04A, 0xD4A017, 4),  // 7  altın sade
        new(false, 0x0B0B0F, 0x14141A, 0x0D0D12, 0x22D3EE, 0x3A86FF, 2),  // 8  camgöbeği çapraz
        new(false, 0x0A0F1E, 0x0F1830, 0x0B1220, 0x6366F1, 0x8B5CF6, 1),  // 9  indigo ızgara
        new(false, 0x0C0C0E, 0x161014, 0x0B0B0D, 0xEC4899, 0x7C5CFF, 3),  // 10 pembe/mor halftone
        new(false, 0x0A100E, 0x101A18, 0x0A0F0E, 0x2EC5B6, 0x22C55E, 5),  // 11 teal/yeşil grafik
        new(false, 0x0B0D12, 0x131722, 0x0A0C10, 0xFF7A18, 0x3A86FF, 4),  // 12 turuncu/mavi sade

        // ---- AÇIK ----
        new(true,  0xFFFFFF, 0xF4F6FA, 0xECEFF4, 0xF26D21, 0xE23B2E, 5),  // 13 beyaz + turuncu grafik (kripto açık)
        new(true,  0xF7F5F2, 0xFBFAF8, 0xF0EEEA, 0xE23B2E, 0xF26D21, 2),  // 14 sıcak beyaz çapraz
        new(true,  0xF2F6FF, 0xFFFFFF, 0xEAF0FF, 0x2563EB, 0x22D3EE, 1),  // 15 soğuk beyaz ızgara
        new(true,  0xFFFFFF, 0xF3FBFA, 0xE9F6F3, 0x14B8A6, 0x2563EB, 0),  // 16 beyaz + teal nokta
        new(true,  0xFAF8FF, 0xFFFFFF, 0xF1ECFF, 0x7C5CFF, 0x6366F1, 3),  // 17 beyaz + mor halftone
        new(true,  0xF5FBF6, 0xFFFFFF, 0xEAF7EC, 0x16A34A, 0x14B8A6, 5),  // 18 beyaz + yeşil grafik
        new(true,  0xFFFDF7, 0xFFFFFF, 0xFBF3E2, 0xD4A017, 0xF26D21, 4),  // 19 krem + altın sade
        new(true,  0xF7F8FA, 0xFFFFFF, 0xEEF1F5, 0x111827, 0xF26D21, 2),  // 20 gri + koyu/turuncu çapraz (haber)
        new(true,  0xFFFFFF, 0xF6F7FB, 0xEDEFF6, 0xDC2626, 0x111827, 1),  // 21 beyaz + kırmızı ızgara
        new(true,  0xF2F7FF, 0xFFFFFF, 0xE6F0FF, 0x06B6D4, 0x2563EB, 0),  // 22 beyaz + camgöbeği nokta
        new(true,  0xFBFAF8, 0xFFFFFF, 0xF2EFEA, 0xEC4899, 0x7C5CFF, 3),  // 23 beyaz + pembe halftone
    };

    private static readonly string[] TrMonths =
        { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
          "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };

    public byte[] RenderTitleCard(string title, string? theme, int width, int height)
    {
        if (width <= 0) width = 1080;
        if (height <= 0) height = 1080;
        var s = MathF.Min(width, height) / 1080f;
        var style = PickStyle(theme, title);

        // Türetilmiş metin renkleri (temaya göre)
        var titleColor = style.Light ? C(0x14181F) : C(0xF4F7FC);
        var mutedColor = style.Light ? C(0x6B7280) : C(0x8A95A8);
        var footerColor = style.Light ? C(0x1F2937) : C(0xDFE7F3);

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;

        DrawBackground(canvas, width, height, s, style);
        DrawTopBar(canvas, width, s, style);

        var margin = 64f * s;
        DrawBrandPill(canvas, margin, s, style);
        DrawDate(canvas, width, margin, s, mutedColor);
        DrawTitleBlock(canvas, title, width, height, s, style, titleColor);
        DrawFooter(canvas, width, height, s, style, footerColor, mutedColor);
        if (!style.Light) DrawVignette(canvas, width, height);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 92);
        return data.ToArray();
    }

    private static Style PickStyle(string? theme, string title)
    {
        if (!string.IsNullOrWhiteSpace(theme) && int.TryParse(theme.Trim(), out var idx) && idx >= 0 && idx < Styles.Length)
            return Styles[idx];
        // Deterministik: başlığa göre stabil ama çeşitli (aynı haber hep aynı, farklı haberler farklı).
        var key = (title ?? "").Trim();
        var hash = 2166136261u;
        foreach (var ch in key) { hash ^= ch; hash *= 16777619u; }
        return Styles[hash % (uint)Styles.Length];
    }

    // ---------- zemin ----------

    private static void DrawBackground(SKCanvas canvas, int w, int h, float s, Style st)
    {
        using (var bg = new SKPaint { IsAntialias = true })
        {
            bg.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(w, h),
                new[] { C(st.Bg1), C(st.Bg2), C(st.Bg3) }, new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawRect(SKRect.Create(0, 0, w, h), bg);
        }

        var acc = C(st.Accent);
        var acc2 = C(st.Accent2);
        var glowA = st.Light ? (byte)0x1A : (byte)0x2C;
        var glowB = st.Light ? (byte)0x14 : (byte)0x24;
        DrawGlow(canvas, new SKPoint(w * 0.84f, h * 0.10f), MathF.Max(w, h) * 0.70f, acc.WithAlpha(glowA));
        DrawGlow(canvas, new SKPoint(w * 0.08f, h * 0.94f), MathF.Max(w, h) * 0.78f, acc2.WithAlpha(glowB));

        DrawPattern(canvas, w, h, s, st);
    }

    private static void DrawPattern(SKCanvas canvas, int w, int h, float s, Style st)
    {
        var line = st.Light ? SKColors.Black.WithAlpha(0x0C) : SKColors.White.WithAlpha(0x0A);
        var acc = C(st.Accent);
        switch (st.Pattern)
        {
            case 0: // nokta dokusu
            {
                using var dot = new SKPaint { IsAntialias = true, Color = line };
                var step = 44f * s; var r = 1.6f * s;
                for (var y = step; y < h; y += step)
                    for (var x = step; x < w; x += step)
                        canvas.DrawCircle(x, y, r, dot);
                break;
            }
            case 1: // ince ızgara
            {
                using var p = new SKPaint { IsAntialias = true, Color = line, Style = SKPaintStyle.Stroke, StrokeWidth = 1f * s };
                var step = 60f * s;
                for (var x = step; x < w; x += step) canvas.DrawLine(x, 0, x, h, p);
                for (var y = step; y < h; y += step) canvas.DrawLine(0, y, w, y, p);
                break;
            }
            case 2: // çapraz aksan çizgileri (sağ üst köşe)
            {
                using var p = new SKPaint { IsAntialias = true, Color = acc.WithAlpha(st.Light ? (byte)0x30 : (byte)0x40), Style = SKPaintStyle.Stroke, StrokeWidth = 6f * s };
                var gap = 26f * s;
                for (var i = 0; i < 7; i++)
                {
                    var off = w * 0.62f + i * gap;
                    canvas.DrawLine(off, 0, off + h * 0.28f, h * 0.28f, p);
                }
                break;
            }
            case 3: // halftone: köşeden içeri küçülen noktalar
            {
                using var dot = new SKPaint { IsAntialias = true, Color = acc.WithAlpha(st.Light ? (byte)0x30 : (byte)0x3A) };
                var cx = w * 0.9f; var cy = h * 0.12f; var step = 30f * s;
                for (var y = 0f; y < h * 0.5f; y += step)
                    for (var x = w * 0.55f; x < w; x += step)
                    {
                        var d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        var rr = MathF.Max(0.5f, 6f * s - d / (60f * s));
                        if (rr > 0.6f) canvas.DrawCircle(x, y, rr, dot);
                    }
                break;
            }
            case 5: // stilize yükselen grafik + soluk barlar (kripto/finans)
            {
                var band = SKRect.Create(w * 0.52f, h * 0.12f, w * 0.42f, h * 0.62f);
                using (var bars = new SKPaint { IsAntialias = true, Color = (st.Light ? SKColors.Black : SKColors.White).WithAlpha(st.Light ? (byte)0x10 : (byte)0x12) })
                {
                    var n = 18; var bw = band.Width / (n * 1.6f);
                    for (var i = 0; i < n; i++)
                    {
                        var t = i / (float)(n - 1);
                        var bh = band.Height * (0.25f + 0.65f * t) * (0.7f + 0.3f * MathF.Abs(MathF.Sin(i * 1.7f)));
                        var x = band.Left + i * bw * 1.6f;
                        canvas.DrawRect(SKRect.Create(x, band.Bottom - bh, bw, bh), bars);
                    }
                }
                using var pathPaint = new SKPaint { IsAntialias = true, Color = acc.WithAlpha(st.Light ? (byte)0xC0 : (byte)0xCC), Style = SKPaintStyle.Stroke, StrokeWidth = 5f * s, StrokeCap = SKStrokeCap.Round };
                using var path = new SKPath();
                var pts = 20;
                for (var i = 0; i < pts; i++)
                {
                    var t = i / (float)(pts - 1);
                    var x = band.Left + t * band.Width;
                    var y = band.Bottom - band.Height * (0.15f + 0.7f * t + 0.06f * MathF.Sin(i * 1.3f));
                    if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
                }
                canvas.DrawPath(path, pathPaint);
                using var dotP = new SKPaint { IsAntialias = true, Color = acc };
                canvas.DrawCircle(band.Right, band.Top + band.Height * 0.15f, 7f * s, dotP);
                break;
            }
            default: break; // 4 sade → yalnız gradyan + ışıma
        }
    }

    private static void DrawGlow(SKCanvas canvas, SKPoint center, float radius, SKColor color)
    {
        using var p = new SKPaint { IsAntialias = true };
        p.Shader = SKShader.CreateRadialGradient(
            center, radius, new[] { color, color.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
        canvas.DrawRect(SKRect.Create(center.X - radius, center.Y - radius, radius * 2, radius * 2), p);
    }

    private static void DrawTopBar(SKCanvas canvas, int w, float s, Style st)
    {
        using var p = new SKPaint { IsAntialias = true };
        p.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(w, 0),
            new[] { C(st.Accent), C(st.Accent2) }, null, SKShaderTileMode.Clamp);
        canvas.DrawRect(SKRect.Create(0, 0, w, 6f * s), p);
    }

    // ---------- üst satır: marka rozeti + tarih ----------

    private void DrawBrandPill(SKCanvas canvas, float margin, float s, Style st)
    {
        var text = BrandText();
        if (text.Length == 0) return;

        using var font = MakeFont(26f * s, SKFontStyleWeight.Bold);
        var tw = font.MeasureText(text);
        var padX = 24f * s;
        var pillH = 54f * s;
        var rect = new SKRect(margin, margin, margin + tw + padX * 2, margin + pillH);
        var rr = pillH / 2;
        var acc = C(st.Accent);

        using (var fill = new SKPaint { IsAntialias = true, Color = acc.WithAlpha(st.Light ? (byte)0x1E : (byte)0x22) })
            canvas.DrawRoundRect(rect, rr, rr, fill);
        using (var stroke = new SKPaint
               { IsAntialias = true, Color = acc.WithAlpha(st.Light ? (byte)0x9C : (byte)0x8C), Style = SKPaintStyle.Stroke, StrokeWidth = 2f * s })
            canvas.DrawRoundRect(rect, rr, rr, stroke);
        using var tp = new SKPaint { IsAntialias = true, Color = st.Light ? Darken(acc) : Brighten(acc) };
        canvas.DrawText(text, rect.Left + padX, rect.MidY + font.Size * 0.36f, font, tp);
    }

    private void DrawDate(SKCanvas canvas, int w, float margin, float s, SKColor muted)
    {
        var now = LocalNow();
        var text = $"{now.Day} {TrMonths[now.Month - 1]} {now.Year}";
        using var font = MakeFont(26f * s, SKFontStyleWeight.Medium);
        using var p = new SKPaint { IsAntialias = true, Color = muted };
        var tw = font.MeasureText(text);
        canvas.DrawText(text, w - margin - tw, margin + 27f * s + font.Size * 0.36f, font, p);
    }

    // ---------- başlık bloğu ----------

    private static void DrawTitleBlock(SKCanvas canvas, string title, int w, int h, float s, Style st, SKColor titleColor)
    {
        title = string.IsNullOrWhiteSpace(title) ? "Başlık" : title.Trim();

        var textX = 104f * s;
        var maxWidth = w - textX - 72f * s;
        var areaTop = 185f * s;
        var areaBottom = h - 165f * s;
        var areaHeight = areaBottom - areaTop;

        List<string> lines = new();
        SKFont? font = null;
        float lineHeight = 0;
        foreach (var size in new[] { 82f, 76f, 70f, 64f, 58f, 52f, 47f, 42f })
        {
            font?.Dispose();
            font = MakeFont(size * s, SKFontStyleWeight.ExtraBold);
            lines = WrapText(title, font, maxWidth);
            lineHeight = font.Size * 1.18f;
            if (lines.Count * lineHeight <= areaHeight) break;
        }
        var maxLines = Math.Max(1, (int)(areaHeight / lineHeight));
        if (lines.Count > maxLines)
        {
            lines = lines.Take(maxLines).ToList();
            lines[^1] = Ellipsize(lines[^1], font!, maxWidth);
        }

        var blockHeight = lines.Count * lineHeight;
        var top = areaTop + (areaHeight - blockHeight) / 2;
        var firstBaseline = top + font!.Size;

        using (var bar = new SKPaint { IsAntialias = true })
        {
            bar.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, top), new SKPoint(0, top + blockHeight),
                new[] { C(st.Accent), C(st.Accent2) }, null, SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SKRect(64f * s, top + 6f * s, 72f * s, top + blockHeight - 2f * s), 4f * s, 4f * s, bar);
        }

        // Gölge yalnız koyu temada (açıkta metin zaten koyu, gölgeye gerek yok).
        if (!st.Light)
        {
            using var shadow = new SKPaint { IsAntialias = true, Color = SKColors.Black.WithAlpha(0x59) };
            shadow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f * s);
            var sy = firstBaseline + 5f * s;
            foreach (var line in lines) { canvas.DrawText(line, textX, sy, font, shadow); sy += lineHeight; }
        }
        using (var tp = new SKPaint { IsAntialias = true, Color = titleColor })
        {
            var y = firstBaseline;
            foreach (var line in lines) { canvas.DrawText(line, textX, y, font, tp); y += lineHeight; }
        }

        using (var under = new SKPaint { IsAntialias = true })
        {
            var uy = top + blockHeight + 36f * s;
            under.Shader = SKShader.CreateLinearGradient(
                new SKPoint(textX, uy), new SKPoint(textX + 150f * s, uy),
                new[] { C(st.Accent), C(st.Accent2) }, null, SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SKRect(textX, uy, textX + 150f * s, uy + 7f * s), 3.5f * s, 3.5f * s, under);
        }
        font.Dispose();
    }

    // ---------- alt bilgi ----------

    private void DrawFooter(SKCanvas canvas, int w, int h, float s, Style st, SKColor footer, SKColor muted)
    {
        var baseline = h - 56f * s;
        var domain = Host();
        var left = domain.Length > 0 ? domain : siteOptions.Value.SiteName;
        if (!string.IsNullOrWhiteSpace(left))
        {
            using (var dot = new SKPaint { IsAntialias = true, Color = C(st.Accent) })
                canvas.DrawCircle(72f * s, baseline - 10f * s, 7f * s, dot);
            using var font = MakeFont(30f * s, SKFontStyleWeight.Bold);
            using var p = new SKPaint { IsAntialias = true, Color = footer };
            canvas.DrawText(left, 94f * s, baseline, font, p);
        }

        var right = siteOptions.Value.SiteName;
        if (!string.IsNullOrWhiteSpace(right) &&
            !string.Equals(right, left, StringComparison.OrdinalIgnoreCase))
        {
            using var font = MakeFont(24f * s, SKFontStyleWeight.Medium);
            using var p = new SKPaint { IsAntialias = true, Color = muted };
            var tw = font.MeasureText(right);
            canvas.DrawText(right, w - 64f * s - tw, baseline, font, p);
        }
    }

    private static void DrawVignette(SKCanvas canvas, int w, int h)
    {
        using var p = new SKPaint { IsAntialias = true };
        p.Shader = SKShader.CreateRadialGradient(
            new SKPoint(w * 0.5f, h * 0.46f), MathF.Max(w, h) * 0.85f,
            new[] { SKColors.Transparent, SKColors.Transparent, SKColors.Black.WithAlpha(0x4D) },
            new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
        canvas.DrawRect(SKRect.Create(0, 0, w, h), p);
    }

    // ---------- yardımcılar ----------

    private static SKColor Brighten(SKColor c) => new(
        (byte)Math.Min(255, c.Red + 45), (byte)Math.Min(255, c.Green + 45), (byte)Math.Min(255, c.Blue + 45));

    private static SKColor Darken(SKColor c) => new(
        (byte)(c.Red * 0.72f), (byte)(c.Green * 0.72f), (byte)(c.Blue * 0.72f));

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

    private DateTime LocalNow()
    {
        var utc = clock.UtcNow.UtcDateTime;
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(id)); }
            catch { /* diğer kimliği dene */ }
        }
        return utc.AddHours(3);
    }

    private static SKFont MakeFont(float size, SKFontStyleWeight weight)
        => new(Fonts.Get(weight), size) { Subpixel = true }; // gömülü Montserrat (bkz. Fonts.cs)

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
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) > maxWidth && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else current = candidate;
        }
        if (current.Length > 0) lines.Add(current);
        return lines.Count > 0 ? lines : new List<string> { text };
    }

    // ==================== ŞABLON ÜZERİNE BİNDİRME (otomatik alan + adaptif renk + rozet) ====================

    public byte[] RenderOnTemplate(byte[] templateBytes, string title, string? badgeText, bool badgeAmber, string? category, int width, int height)
    {
        if (width <= 0) width = 1080;
        if (height <= 0) height = 1080;
        var s = MathF.Min(width, height) / 1080f;
        title = string.IsNullOrWhiteSpace(title) ? "Başlık" : title.Trim();

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;

        // 1) Şablonu cover-crop çiz (çözülemezse koyu zemin)
        var decoded = SKBitmap.Decode(templateBytes);
        try
        {
            if (decoded is null) canvas.Clear(new SKColor(0x11, 0x14, 0x1A));
            else
            {
                var sc = MathF.Max((float)width / decoded.Width, (float)height / decoded.Height);
                var dw = decoded.Width * sc; var dh = decoded.Height * sc;
                var dst = new SKRect((width - dw) / 2f, (height - dh) / 2f, (width + dw) / 2f, (height + dh) / 2f);
                using var img = SKImage.FromBitmap(decoded);
                using var bp = new SKPaint { IsAntialias = true };
                canvas.DrawImage(img, dst, new SKSamplingOptions(SKCubicResampler.Mitchell), bp);
            }
        }
        finally { decoded?.Dispose(); }

        // 2) Kompoze görselden küçük gri + busyness (integral) haritaları — otomatik alan/renk için
        var aw = 180;
        var ah = Math.Max(1, (int)Math.Round((double)height / width * aw));
        var (busyI, lumI) = AnalyzeSurface(surface, aw, ah);

        // 3) Üst (kategori) rezervi + en boş metin kutusu
        var hasCat = !string.IsNullOrWhiteSpace(category);
        var hasBadge = !string.IsNullOrWhiteSpace(badgeText);
        var top = 60f * s;
        var catPillH = 34f * s + 14f * s * 2f;
        var reservedTop = top + (hasCat ? catPillH + 20f * s : 0f);

        var (boxX0, boxY0, boxX1, boxY1) = FindTextZone(width, height, busyI, aw, ah, reservedTop);
        var boxW = boxX1 - boxX0; var boxH = boxY1 - boxY0;

        var badgeStrip = hasBadge ? 96f * s : 0f;
        var textAreaH = boxH - badgeStrip;

        // 4) Başlığı sığdır (Montserrat ExtraBold) — satır satır adaptif renk + zıt renkli hafif kontur
        var padX = 46f * s;
        var maxWidth = boxW - padX * 2f;
        var pad = 18f * s;
        List<string> lines = new(); SKFont? font = null; float lineHeight = 0;
        foreach (var size in new[] { 86f, 80f, 74f, 68f, 62f, 56f, 50f, 46f, 42f })
        {
            font?.Dispose();
            font = MakeFont(size * s, SKFontStyleWeight.ExtraBold);
            lines = WrapText(title, font, maxWidth);
            lineHeight = font.Size * 1.16f;
            if (lines.Count * lineHeight <= textAreaH - 2f * pad) break;
        }
        var maxLines = Math.Max(1, (int)((textAreaH - 2f * pad) / lineHeight));
        if (lines.Count > maxLines) { lines = lines.Take(maxLines).ToList(); lines[^1] = Ellipsize(lines[^1], font!, maxWidth); }

        var blockH = lines.Count * lineHeight;
        var textTop = boxY0 + (textAreaH - blockH) / 2f;
        var textX = boxX0 + padX;
        var strokeW = MathF.Max(2f, font!.Size * 0.035f);
        var baseline = textTop + font.Size;
        foreach (var line in lines)
        {
            var lw = font.MeasureText(line);
            var ll = RegionMean(lumI, aw, ah, width, height, textX, baseline - font.Size, lw, lineHeight);
            var dark = ll < 150f;
            var fill = dark ? new SKColor(0xFF, 0xFF, 0xFF) : new SKColor(0x11, 0x14, 0x1A);
            var halo = dark ? SKColors.Black.WithAlpha(0x66) : SKColors.White.WithAlpha(0x82);
            using (var hp = new SKPaint { IsAntialias = true, Color = halo, Style = SKPaintStyle.Stroke, StrokeWidth = strokeW, StrokeJoin = SKStrokeJoin.Round })
                canvas.DrawText(line, textX, baseline, font, hp);
            using (var tp = new SKPaint { IsAntialias = true, Color = fill })
                canvas.DrawText(line, textX, baseline, font, tp);
            baseline += lineHeight;
        }
        font.Dispose();

        // 5) Kategori etiketi (sol üst, küçük, turuncu gradyan)
        if (hasCat)
        {
            var cat = category!.Trim().ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
            using var cf = MakeFont(34f * s, SKFontStyleWeight.SemiBold);
            DrawPill(canvas, 64f * s, top, cat, cf, C(0xF26D21), C(0xE63B2E), glow: null,
                textCol: new SKColor(0xFF, 0xFF, 0xFF), padX: 26f * s, padY: 14f * s, radius: 12f * s, centerX: null);
        }

        // 6) Dikkat rozeti (başlığın ALTINDA ORTADA, premium: gradyan + ışıma)
        if (hasBadge)
        {
            var bText = badgeText!.Trim().ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
            using var bf = MakeFont(48f * s, SKFontStyleWeight.ExtraBold);
            var (g1, g2, glow) = badgeAmber
                ? (C(0xE08A12), C(0xF5A524), C(0xF5A524))
                : (C(0xC61824), C(0xF03A32), C(0xE41E2C));
            var by = boxY0 + textAreaH + 6f * s;
            DrawPill(canvas, 0f, by, bText, bf, g1, g2, glow: glow.WithAlpha(0xAA),
                textCol: new SKColor(0xFF, 0xFF, 0xFF), padX: 38f * s, padY: 18f * s, radius: 16f * s, centerX: width / 2f);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 92);
        return data.ToArray();
    }

    /// <summary>Kompoze yüzeyden küçük gri + busyness'in integral görüntülerini üretir (otomatik alan/renk analizi).</summary>
    private static (float[] BusyIntegral, float[] LumIntegral) AnalyzeSurface(SKSurface surface, int aw, int ah)
    {
        using var snap = surface.Snapshot();
        using var small = new SKBitmap(new SKImageInfo(aw, ah, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c2 = new SKCanvas(small))
        {
            c2.Clear(SKColors.Black);
            c2.DrawImage(snap, new SKRect(0, 0, aw, ah), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
        var lum = new float[aw * ah];
        for (var y = 0; y < ah; y++)
            for (var x = 0; x < aw; x++)
            {
                var p = small.GetPixel(x, y);
                lum[y * aw + x] = 0.299f * p.Red + 0.587f * p.Green + 0.114f * p.Blue;
            }
        var busy = new float[aw * ah];
        for (var y = 0; y < ah; y++)
            for (var x = 0; x < aw; x++)
            {
                var c = lum[y * aw + x];
                var dx = x > 0 ? MathF.Abs(c - lum[y * aw + x - 1]) : 0f;
                var dy = y > 0 ? MathF.Abs(c - lum[(y - 1) * aw + x]) : 0f;
                busy[y * aw + x] = dx + dy;
            }
        return (Integral(busy, aw, ah), Integral(lum, aw, ah));
    }

    private static float[] Integral(float[] a, int w, int h)
    {
        var integral = new float[(w + 1) * (h + 1)];
        for (var y = 1; y <= h; y++)
        {
            float row = 0;
            for (var x = 1; x <= w; x++)
            {
                row += a[(y - 1) * w + x - 1];
                integral[y * (w + 1) + x] = integral[(y - 1) * (w + 1) + x] + row;
            }
        }
        return integral;
    }

    private static float BoxMean(float[] integral, int aw, int ah, int ax0, int ay0, int ax1, int ay1)
    {
        ax0 = Math.Clamp(ax0, 0, aw); ax1 = Math.Clamp(ax1, 0, aw);
        ay0 = Math.Clamp(ay0, 0, ah); ay1 = Math.Clamp(ay1, 0, ah);
        if (ax1 <= ax0 || ay1 <= ay0) return 128f;
        var st = aw + 1;
        var area = (ax1 - ax0) * (ay1 - ay0);
        return (integral[ay1 * st + ax1] - integral[ay0 * st + ax1] - integral[ay1 * st + ax0] + integral[ay0 * st + ax0]) / area;
    }

    /// <summary>Tam-çözünürlük bir dikdörtgenin analiz haritasındaki ortalama değeri (busyness ya da luminance).</summary>
    private static float RegionMean(float[] integral, int aw, int ah, int w, int h, float fx0, float fy0, float fw, float fh)
        => BoxMean(integral, aw, ah,
            (int)(fx0 * aw / w), (int)(fy0 * ah / h),
            (int)MathF.Ceiling((fx0 + fw) * aw / w), (int)MathF.Ceiling((fy0 + fh) * ah / h));

    /// <summary>En boş (düşük busyness) dikdörtgeni bulur; sağ-alt filigran cezalı, üst hafif tercihli.</summary>
    private static (float X0, float Y0, float X1, float Y1) FindTextZone(int w, int h, float[] busyI, int aw, int ah, float yMin)
    {
        var bw = w * 0.86f;
        var bh = h * (h <= w ? 0.42f : 0.32f);
        var mx = w * 0.06f;
        var xs = new[] { mx, (w - bw) / 2f, w - bw - mx };
        var yLo = MathF.Max(h * 0.06f, yMin);
        var yHi = MathF.Max(yLo, h - bh - h * 0.08f);
        var bestScore = float.MaxValue; float bx = mx, by = yLo;
        for (var i = 0; i < 14; i++)
        {
            var y0 = yLo + (yHi - yLo) * (i / 13f);
            foreach (var x0 in xs)
            {
                if (x0 < 0f || x0 > w - bw) continue;
                var score = RegionMean(busyI, aw, ah, w, h, x0, y0, bw, bh);
                if (x0 + bw > w * 0.55f && y0 + bh > h * 0.90f) score += 40f; // filigran (sağ-alt) cezası
                score += (y0 / h) * 3f;                                        // üst tercih
                if (score < bestScore) { bestScore = score; bx = x0; by = y0; }
            }
        }
        return (bx, by, bx + bw, by + bh);
    }

    /// <summary>Yuvarlak etiket (pill): gradyan dolgu + opsiyonel ışıma; sola hizalı (centerX=null) ya da ortalı.</summary>
    private void DrawPill(SKCanvas canvas, float x, float y, string text, SKFont font, SKColor g1, SKColor g2, SKColor? glow, SKColor textCol, float padX, float padY, float radius, float? centerX)
    {
        var tw = font.MeasureText(text);
        var w = tw + padX * 2f;
        var hgt = font.Size + padY * 2f;
        var left = centerX is { } cx ? cx - w / 2f : x;
        var rect = new SKRect(left, y, left + w, y + hgt);
        if (glow is { } gl)
        {
            using var gp = new SKPaint { IsAntialias = true, Color = gl };
            gp.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18f);
            canvas.DrawRoundRect(rect, radius, radius, gp);
        }
        using (var fill = new SKPaint { IsAntialias = true })
        {
            fill.Shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
                new[] { g1, g2 }, null, SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(rect, radius, radius, fill);
        }
        using var tp = new SKPaint { IsAntialias = true, Color = textCol };
        canvas.DrawText(text, rect.Left + padX, rect.MidY + font.Size * 0.36f, font, tp);
    }

}
