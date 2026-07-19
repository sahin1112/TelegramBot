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
}
