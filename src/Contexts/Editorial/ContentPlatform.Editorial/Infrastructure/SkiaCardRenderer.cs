using ContentPlatform.Abstractions;
using SkiaSharp;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Başlığı koyu zemin üzerine ortalayarak bir kart görsele basar (SkiaSharp 3.x — SKFont API).
/// </summary>
internal sealed class SkiaCardRenderer : ICardRenderer
{
    public byte[] RenderTitleCard(string title, string? theme, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x14, 0x18, 0x24)); // koyu lacivert zemin

        using (var accent = new SKPaint { Color = new SKColor(0x2E, 0xC5, 0xB6), IsAntialias = true })
            canvas.DrawRect(0, 0, width, 10, accent);

        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                             ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 56f);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };

        var lines = WrapText(title, font, width - 160);
        var lineHeight = font.Size * 1.25f;
        var totalHeight = lines.Count * lineHeight;
        var y = (height - totalHeight) / 2 + font.Size;

        foreach (var line in lines)
        {
            var textWidth = font.MeasureText(line);
            canvas.DrawText(line, (width - textWidth) / 2, y, font, paint);
            y += lineHeight;
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
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
