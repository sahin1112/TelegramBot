using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ContentPlatform.Api.Diagnostics;

internal static class DiagnosticsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // ---------------------------------------------------------------------
        // ZİYARETÇİYE KAPALI, gizli anahtarla erişilen BİRLEŞİK (Api+Worker) log akışı.
        // /api/v1 ALTINDA DEĞİL -> AuthMiddleware'e takılmaz; güvenlik gizli anahtarda.
        // Örn: https://hermasadabiz.com/_diag/logs?key=...&minutes=10
        //   opsiyonel: &level=error  &source=worker  &q=arama  &format=json
        // ---------------------------------------------------------------------
        app.MapGet("/_diag/logs", (HttpContext ctx, IOptions<DiagnosticsOptions> opt, LogFeedReader reader) =>
        {
            var o = opt.Value;
            ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            ctx.Response.Headers.CacheControl = "no-store";

            var key = ctx.Request.Query["key"].ToString();
            if (!o.EnableLogFeed || string.IsNullOrEmpty(o.LogFeedKey) || !KeyMatches(key, o.LogFeedKey))
                return Results.NotFound(); // anahtar yok/yanlış -> hiç yokmuş gibi davran

            var minutes = ClampInt(ctx.Request.Query["minutes"], 10, 1, Math.Max(1, o.FeedMaxMinutes));
            var minRank = ParseLevelRank(ctx.Request.Query["level"]);
            var search = ctx.Request.Query["q"].ToString();
            var sourceFilter = ctx.Request.Query["source"].ToString();
            var limit = ClampInt(ctx.Request.Query["limit"], 3000, 1, 10000);
            var since = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(minutes);

            IReadOnlyList<LogFeedEntry> entries =
                reader.Read(since, minRank, string.IsNullOrWhiteSpace(search) ? null : search, limit);
            if (sourceFilter is "api" or "worker")
                entries = entries.Where(e => e.Source == sourceFilter).ToList();

            if (string.Equals(ctx.Request.Query["format"], "json", StringComparison.OrdinalIgnoreCase))
                return Results.Json(new
                {
                    generatedAt = DateTimeOffset.UtcNow,
                    windowMinutes = minutes,
                    count = entries.Count,
                    entries
                });

            // Varsayılan: düz metin — WebFetch ile okumaya en uygun format.
            var sb = new StringBuilder();
            sb.Append("# hermasadabiz.com log akisi — son ").Append(minutes).Append(" dk — ")
              .Append(entries.Count).Append(" kayit (uretim ")
              .Append(DateTimeOffset.UtcNow.ToString("o")).Append(")\n");
            sb.Append("# kaynak etiketi: [API] = web uygulamasi, [WRK] = worker/Windows servisi\n");
            if (entries.Count == 0)
                sb.Append("(bu pencerede kayit yok)\n");
            foreach (var e in entries)
            {
                var tag = e.Source == "worker" ? "WRK" : "API";
                sb.Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                  .Append(" [").Append(tag).Append("] [").Append(ShortLevel(e.Level)).Append("] ");
                if (!string.IsNullOrEmpty(e.Context)) sb.Append(TrimCtx(e.Context!)).Append(": ");
                sb.Append(e.Message).Append('\n');
                if (!string.IsNullOrEmpty(e.Exception)) sb.Append(e.Exception).Append('\n');
            }
            return Results.Text(sb.ToString(), "text/plain; charset=utf-8");
        }).ExcludeFromDescription();

        // ---------------------------------------------------------------------
        // Panel için (admin token ile KORUMALI — AuthMiddleware /api/v1/* korur) canlı log JSON'u.
        // ---------------------------------------------------------------------
        app.MapGet("/api/v1/logs", (HttpContext ctx, LogFeedReader reader) =>
        {
            var minutes = ClampInt(ctx.Request.Query["minutes"], 15, 1, 240);
            var minRank = ParseLevelRank(ctx.Request.Query["level"]);
            var search = ctx.Request.Query["q"].ToString();
            var sourceFilter = ctx.Request.Query["source"].ToString();
            var limit = ClampInt(ctx.Request.Query["limit"], 1000, 1, 5000);
            var since = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(minutes);

            IReadOnlyList<LogFeedEntry> entries =
                reader.Read(since, minRank, string.IsNullOrWhiteSpace(search) ? null : search, limit);
            if (sourceFilter is "api" or "worker")
                entries = entries.Where(e => e.Source == sourceFilter).ToList();

            return Results.Json(new { count = entries.Count, entries });
        });
    }

    private static bool KeyMatches(string provided, string expected)
    {
        var a = Encoding.UTF8.GetBytes(provided ?? "");
        var b = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static int ClampInt(StringValues raw, int def, int min, int max)
        => int.TryParse(raw.ToString(), out var v) ? Math.Clamp(v, min, max) : def;

    private static int? ParseLevelRank(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return null;
        return level.Trim().ToLowerInvariant() switch
        {
            "verbose" or "trace" or "vrb" => 0,
            "debug" or "dbg" => 1,
            "info" or "information" or "inf" => 2,
            "warn" or "warning" or "wrn" => 3,
            "error" or "err" => 4,
            "fatal" or "ftl" or "critical" => 5,
            _ => null
        };
    }

    private static string ShortLevel(string level) => level.ToLowerInvariant() switch
    {
        "verbose" => "VRB",
        "debug" => "DBG",
        "information" => "INF",
        "warning" => "WRN",
        "error" => "ERR",
        "fatal" => "FTL",
        _ => level
    };

    private static string TrimCtx(string ctx)
    {
        var i = ctx.LastIndexOf('.');
        return i >= 0 && i < ctx.Length - 1 ? ctx[(i + 1)..] : ctx;
    }
}
