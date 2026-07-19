using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog.Events;
using Serilog.Formatting;

namespace ContentPlatform.Logging;

/// <summary>
/// Her log kaydını TEK satırlık JSON olarak yazar (satır başına bir olay).
/// Api ve Worker bu formatı ORTAK bir klasöre yazar; /_diag/logs ucu bu dosyaları
/// okuyup Api + Worker loglarını birleştirir. İstisnalar tek satırda (kaçışlı) tutulur,
/// böylece dosya güvenle satır satır okunabilir.
/// Bu dosya hem Api hem Worker projesine "linked Compile" ile dahil edilir.
/// </summary>
public sealed class JsonLogFormatter : ITextFormatter
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public void Format(LogEvent logEvent, TextWriter output)
    {
        string? source = null;
        if (logEvent.Properties.TryGetValue("SourceContext", out var sc)
            && sc is ScalarValue { Value: string s })
            source = s;

        var record = new LogLine
        {
            t = logEvent.Timestamp.ToString("o"),
            l = logEvent.Level.ToString(),
            m = logEvent.RenderMessage(),
            s = source,
            x = logEvent.Exception?.ToString()
        };

        // JsonSerializer varsayılan olarak tek satır üretir; mesaj/istisna içindeki
        // satır sonları \n olarak kaçışlanır -> fiziksel olarak tek satır garanti.
        output.WriteLine(JsonSerializer.Serialize(record, Opts));
    }

    private sealed class LogLine
    {
        public string t { get; set; } = "";
        public string l { get; set; } = "";
        public string m { get; set; } = "";
        public string? s { get; set; }
        public string? x { get; set; }
    }
}
