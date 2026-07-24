using ContentPlatform.Editorial.Application;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Editorial.Infrastructure;

/// <summary>
/// Görsel şablon kütüphanesini diskten okur. Kök, MediaOptions.CardAssetsPath'ten çözülür.
/// Göreli verilirse (varsayılan "wwwroot/assets/cards") hem Api hem Worker'dan bulunabilsin diye
/// uygulama klasörü + çalışma dizini + üst klasörler taranır; ayrıca repo yapısındaki
/// src/Host/ContentPlatform.Api/wwwroot/assets/cards yolu da denenir (Worker bunu paylaşır).
/// YAYINDA: Media:CardAssetsPath'i Api+Worker'ın erişebileceği MUTLAK ortak yola ayarlayın (Media:StoragePath gibi).
/// </summary>
internal sealed class CardAssetLibrary(IOptions<MediaOptions> options) : ICardAssetLibrary
{
    private readonly string _configured = options.Value.CardAssetsPath;

    private IEnumerable<string> Candidates()
    {
        var c = string.IsNullOrWhiteSpace(_configured) ? "wwwroot/assets/cards" : _configured;
        if (Path.IsPathRooted(c)) { yield return c; yield break; }

        // Repo yapısındaki Api wwwroot yolu (Worker + repo kökünden çalıştırma için).
        var apiRel = Path.Combine("src", "Host", "ContentPlatform.Api", "wwwroot", "assets", "cards");

        var roots = new List<string>();
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = start;
            for (var i = 0; i < 9 && !string.IsNullOrEmpty(dir); i++)
            {
                roots.Add(dir);
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }
        foreach (var r in roots)
        {
            yield return Path.Combine(r, c);       // {kök}/wwwroot/assets/cards
            yield return Path.Combine(r, apiRel);  // {kök}/src/Host/ContentPlatform.Api/wwwroot/assets/cards
        }
    }

    private static bool HasKinds(string dir)
    {
        try { return Directory.Exists(Path.Combine(dir, "1x1")) || Directory.Exists(Path.Combine(dir, "reels")); }
        catch { return false; }
    }

    /// <summary>1x1/reels alt klasörü GERÇEKTEN olan ilk kök. Verilen yolun bir alt "cards" klasörü de denenir
    /// (CardAssetsPath ...\assets olsa da ...\assets\cards olsa da bulunur).</summary>
    private string? Root()
    {
        string? firstExisting = null;
        foreach (var cand in Candidates())
        {
            try
            {
                if (HasKinds(cand)) return cand;
                var nested = Path.Combine(cand, "cards");
                if (HasKinds(nested)) return nested;
                if (firstExisting is null && Directory.Exists(cand)) firstExisting = cand;
            }
            catch { /* geçersiz yol */ }
        }
        return firstExisting;
    }

    public IReadOnlyList<string> List(string kind)
    {
        var root = Root();
        if (root is null) return Array.Empty<string>();
        var dir = Path.Combine(root, SafeKind(kind));
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir)
            .Where(IsImage)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public byte[]? Read(string kind, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
            return null;
        var root = Root();
        if (root is null) return null;
        var path = Path.Combine(root, SafeKind(kind), fileName);
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch { return null; }
    }

    private static string SafeKind(string kind) => string.Equals(kind, "reels", StringComparison.OrdinalIgnoreCase) ? "reels" : "1x1";
    private static bool IsImage(string f)
    {
        var e = Path.GetExtension(f).ToLowerInvariant();
        return e is ".png" or ".jpg" or ".jpeg" or ".webp";
    }
}
