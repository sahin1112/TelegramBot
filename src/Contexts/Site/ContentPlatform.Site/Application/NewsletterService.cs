using System.Text.RegularExpressions;
using ContentPlatform.Site.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Site.Application;

/// <summary>
/// Bülten aboneliği. E-postayı Site şemasında saklar. Tablo yoksa oluşturur (migration'sız,
/// idempotent) — böylece yeni bir EF migration'a gerek kalmaz. Aynı e-posta tekrar eklenmez.
/// </summary>
public sealed class NewsletterService(SiteDbContext db, ILogger<NewsletterService> logger)
{
    private static readonly Regex EmailRx =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private static bool _tableReady;

    public async Task<bool> SubscribeAsync(string? email, CancellationToken ct)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        if (email.Length is 0 or > 200 || !EmailRx.IsMatch(email)) return false;

        try
        {
            await EnsureTableAsync(ct);
            // Aynı e-posta tekrar eklenmesin (benzersiz indeks + IF NOT EXISTS).
            var sql = @"IF NOT EXISTS (SELECT 1 FROM [site].[newsletter_subscribers] WHERE [Email] = {0})
                        INSERT INTO [site].[newsletter_subscribers] ([Id],[Email],[CreatedAt]) VALUES (NEWID(), {0}, SYSUTCDATETIME());";
            await db.Database.ExecuteSqlRawAsync(sql, new object[] { email }, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bülten kaydı başarısız: {Email}", email);
            return false;
        }
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (_tableReady) return;
        var sql = @"IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'site') EXEC('CREATE SCHEMA [site]');
IF OBJECT_ID('[site].[newsletter_subscribers]') IS NULL
BEGIN
    CREATE TABLE [site].[newsletter_subscribers](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [Email] NVARCHAR(200) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL);
    CREATE UNIQUE INDEX [ux_newsletter_email] ON [site].[newsletter_subscribers]([Email]);
END";
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        _tableReady = true;
    }
}
