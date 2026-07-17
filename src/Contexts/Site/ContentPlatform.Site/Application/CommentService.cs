using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ContentPlatform.Site.Domain;
using ContentPlatform.Site.Infrastructure;
using ContentPlatform.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Site.Application;

/// <summary>Blog yorumları: gönderim (sanitize + spam sezgisi → Pending/Spam) ve moderasyon.</summary>
public sealed class CommentService(SiteDbContext db, IClock clock)
{
    private static readonly string[] SpamWords =
        { "viagra", "casino", "porn", "escort", "loan", "bitcoin profit", "crypto invest", "seo service", "buy now", "cheap " };

    /// <summary>Public gönderim. Boş/kısa reddedilir; spam sezgisi Spam'e, aksi Pending'e düşürür.</summary>
    public async Task<Result> SubmitAsync(Guid blogPostId, string? name, string? email, string? body, string? ip, CancellationToken ct)
    {
        name = (name ?? "").Trim();
        var clean = PlainText(body ?? "");
        if (name.Length == 0 || clean.Length < 2)
            return Result.Failure(Error.Validation("Ad ve yorum gerekli."));
        if (name.Length > 80) name = name[..80];
        if (clean.Length > 4000) clean = clean[..4000];

        var status = IsSpam(clean) ? CommentStatus.Spam : CommentStatus.Pending;
        var comment = new Comment(blogPostId, name,
            string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            clean, status, HashIp(ip), clock);

        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<bool> ModerateAsync(Guid id, CommentStatus status, CancellationToken ct)
    {
        var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return false;
        c.Moderate(status, clock);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Toplu moderasyon — seçilen yorumların hepsini tek durumla günceller.</summary>
    public async Task<int> ModerateManyAsync(IReadOnlyList<Guid> ids, CommentStatus status, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return 0;
        var items = await db.Comments.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        foreach (var c in items) c.Moderate(status, clock);
        await db.SaveChangesAsync(ct);
        return items.Count;
    }

    /// <summary>Moderasyon listesi (durum filtreli + arama, sunucu tarafında sayfalı).</summary>
    public async Task<(IReadOnlyList<CommentModerationDto> Items, int Total)> ListPagedAsync(CommentStatus? status, string? search, int page, int size, CancellationToken ct)
    {
        var q = from c in db.Comments.AsNoTracking()
                join p in db.BlogPosts.AsNoTracking() on c.BlogPostId equals p.Id
                select new { c, p };
        if (status is { } st) q = q.Where(x => x.c.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => x.c.AuthorName.Contains(term)
                             || (x.c.AuthorEmail != null && x.c.AuthorEmail.Contains(term))
                             || x.c.Body.Contains(term)
                             || x.p.Title.Contains(term));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.c.CreatedAt)
            .Skip(Math.Max(0, page - 1) * size).Take(size)
            .Select(x => new CommentModerationDto(x.c.Id, x.p.Title, x.p.Slug, x.c.AuthorName, x.c.AuthorEmail, x.c.Body, x.c.Status, x.c.CreatedAt))
            .ToListAsync(ct);
        return (items, total);
    }

    /// <summary>Bir yazının onaylı yorumları (public gösterim).</summary>
    public async Task<IReadOnlyList<CommentView>> ApprovedForPostAsync(Guid blogPostId, CancellationToken ct) =>
        await db.Comments.AsNoTracking()
            .Where(c => c.BlogPostId == blogPostId && c.Status == CommentStatus.Approved)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentView(c.AuthorName, c.Body, c.CreatedAt))
            .ToListAsync(ct);

    public Task<int> PendingCountAsync(CancellationToken ct) =>
        db.Comments.CountAsync(c => c.Status == CommentStatus.Pending, ct);

    // ---- yardımcılar ----
    private static string PlainText(string input)
    {
        var noTags = Regex.Replace(input, "<.*?>", " ");            // etiketleri ayıkla (XSS'e kapı yok)
        return Regex.Replace(noTags, @"\s+", " ").Trim();
    }

    private static bool IsSpam(string text)
    {
        var lower = text.ToLowerInvariant();
        var links = Regex.Matches(lower, @"https?://").Count;
        if (links >= 3) return true;
        return SpamWords.Any(w => lower.Contains(w, StringComparison.Ordinal));
    }

    private static string HashIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "unknown";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexString(bytes)[..32];
    }
}

public sealed record CommentView(string AuthorName, string Body, DateTimeOffset CreatedAt);
public sealed record CommentModerationDto(Guid Id, string PostTitle, string PostSlug, string AuthorName, string? AuthorEmail, string Body, CommentStatus Status, DateTimeOffset CreatedAt);
public sealed record BulkModerateRequest(IReadOnlyList<Guid> Ids, CommentStatus Status);
