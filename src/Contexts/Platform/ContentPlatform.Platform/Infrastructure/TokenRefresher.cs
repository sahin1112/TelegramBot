using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Application;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>
/// Süresi yaklaşan IG/Threads token'larını yeniler (bkz. docs/03 §4).
/// Gerçek platform API çağrısı sıralı geliştirmede (08/09) eklenecek; burada iş akışı + hata işaretleme hazır.
/// </summary>
internal sealed class TokenRefresher(
    ISocialAccountRepository repository,
    IClock clock,
    ILogger<TokenRefresher> logger) : ITokenRefresher
{
    private static readonly TimeSpan RefreshThreshold = TimeSpan.FromDays(7);

    public async Task<int> RefreshDueAsync(CancellationToken ct)
    {
        var accounts = await repository.ListAsync(ct);
        var due = accounts.Where(a => a.NeedsRefresh(clock, RefreshThreshold)).ToList();
        var refreshed = 0;

        foreach (var account in due)
        {
            try
            {
                // TODO(08/09): platforma göre gerçek uzun-ömürlü token yenileme çağrısı.
                // Şimdilik yalnız "kontrol edildi" işaretlenir; gerçek çağrı gelince UpdateToken kullanılacak.
                account.MarkChecked(clock);
                logger.LogInformation("Token yenileme sırada (stub): {Platform} {Id}", account.Platform, account.Id);
                refreshed++;
            }
            catch (Exception ex)
            {
                account.MarkError($"Token yenilenemedi: {ex.Message}", clock);
                logger.LogWarning(ex, "Token yenileme hatası: {Id}", account.Id);
            }
        }

        if (due.Count > 0) await repository.SaveChangesAsync(ct);
        return refreshed;
    }
}
