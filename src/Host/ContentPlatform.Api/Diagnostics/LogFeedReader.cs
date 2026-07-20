using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ContentPlatform.Api.Diagnostics;

/// <summary>Birleştirilmiş log akışındaki tek kayıt.</summary>
public sealed record LogFeedEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Source,      // "api" | "worker"
    string Message,
    string? Exception,
    string? Context);

/// <summary>
/// Ortak klasördeki api-*.clef ve worker-*.clef dosyalarını okuyup zaman penceresine göre
/// birleştirir. Serilog dosyaları yazarken açık tuttuğu için FileShare.ReadWrite ile okur.
/// Güvenlik/performans için her dosyanın yalnızca son ~4 MB'ı taranır.
/// </summary>
public sealed class LogFeedReader
{
    private readonly DiagnosticsOptions _opt;
    public LogFeedReader(IOptions<DiagnosticsOptions> opt) => _opt = opt.Value;

    private static readonly string[] Levels =
        { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" };

    public static int LevelRank(string level)
    {
        for (var i = 0; i < Levels.Length; i++)
            if (string.Equals(Levels[i], level, StringComparison.OrdinalIgnoreCase))
                return i;
        return 2; // bilinmeyen -> Information kabul et
    }

    public IReadOnlyList<LogFeedEntry> Read(DateTimeOffset since, int? minLevelRank, string? search, int limit)
    {
        var dir = _opt.LogDirectory;
        var all = new List<LogFeedEntry>(256);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return all;

        foreach (var (prefix, source) in new[] { ("api-", "api"), ("worker-", "worker") })
        {
            string[] files;
            try { files = Directory.GetFiles(dir, prefix + "*.clef"); }
            catch { continue; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            // En yeni 2 dosya (gece yarısı devri penceresi için yeter)
            foreach (var path in Enumerable.Reverse(files).Take(2)) // LINQ açık çağrı: C# 14 span dönüşümü array.Reverse()i void MemoryExtensions.Reverse(Span)a bağlıyor
                ReadFileTail(path, source, since, minLevelRank, search, all);
        }

        all.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        if (limit > 0 && all.Count > limit)
            return all.GetRange(all.Count - limit, limit);
        return all;
    }

    private static void ReadFileTail(string path, string source, DateTimeOffset since,
        int? minLevelRank, string? search, List<LogFeedEntry> sink)
    {
        const long MaxBytes = 4 * 1024 * 1024;
        byte[] buf;
        long start;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            start = Math.Max(0, fs.Length - MaxBytes);
            fs.Seek(start, SeekOrigin.Begin);
            var len = (int)(fs.Length - start);
            buf = new byte[len];
            var read = 0;
            while (read < len)
            {
                var n = fs.Read(buf, read, len - read);
                if (n <= 0) break;
                read += n;
            }
            if (read != len) Array.Resize(ref buf, read);
        }
        catch { return; }

        var text = Encoding.UTF8.GetString(buf);
        var lines = text.Split('\n');
        // Dosyanın ortasından başladıysak ilk satır yarım olabilir -> atla.
        var startIdx = start > 0 ? 1 : 0;
        for (var i = startIdx; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] != '{') continue;

            var entry = TryParse(line, source);
            if (entry is null) continue;
            if (entry.Timestamp < since) continue;
            if (minLevelRank is { } mr && LevelRank(entry.Level) < mr) continue;

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                var hit = entry.Message.Contains(q, StringComparison.OrdinalIgnoreCase)
                          || (entry.Exception?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                          || (entry.Context?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
                if (!hit) continue;
            }
            sink.Add(entry);
        }
    }

    private static LogFeedEntry? TryParse(string line, string source)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("t", out var tEl)) return null;
            if (!DateTimeOffset.TryParse(tEl.GetString(), out var ts)) return null;

            var level = root.TryGetProperty("l", out var lEl) ? lEl.GetString() ?? "Information" : "Information";
            var msg = root.TryGetProperty("m", out var mEl) ? mEl.GetString() ?? "" : "";
            var ex = root.TryGetProperty("x", out var xEl) ? xEl.GetString() : null;
            var ctx = root.TryGetProperty("s", out var sEl) ? sEl.GetString() : null;
            return new LogFeedEntry(ts, level, source, msg, ex, ctx);
        }
        catch { return null; }
    }
}
