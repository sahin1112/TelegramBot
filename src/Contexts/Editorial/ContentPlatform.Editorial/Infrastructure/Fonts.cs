using System.Collections.Concurrent;
using SkiaSharp;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Gömülü Montserrat fontlarını (ExtraBold/SemiBold/Medium) yükler — başlık ve rozetlerde tutarlı,
/// "göze hitap eden" tipografi için. Font gömülü kaynaktan bir kez okunur ve önbelleğe alınır.
/// Yüklenemezse sistem fontuna (Segoe UI/Arial) düşer — asla patlamaz.
/// </summary>
internal static class Fonts
{
    private static readonly ConcurrentDictionary<string, SKTypeface?> Cache = new();

    private static SKTypeface? Load(string suffix) => Cache.GetOrAdd(suffix, s =>
    {
        try
        {
            var asm = typeof(Fonts).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(s, StringComparison.OrdinalIgnoreCase));
            if (name is null) return null;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            using var data = SKData.CreateCopy(ms.ToArray());
            return SKTypeface.FromData(data); // FromData veriyi kopyalar; stream/data güvenle serbest bırakılır
        }
        catch { return null; }
    });

    /// <summary>İstenen ağırlığa en yakın Montserrat; yoksa sistem fontu.</summary>
    public static SKTypeface Get(SKFontStyleWeight weight)
    {
        var w = (int)weight;
        var tf = w >= 700 ? Load("Montserrat-ExtraBold.ttf")
               : w >= 500 ? Load("Montserrat-SemiBold.ttf")
               :            Load("Montserrat-Medium.ttf");
        tf ??= SKTypeface.FromFamilyName("Segoe UI", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Arial", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
        return tf;
    }
}
