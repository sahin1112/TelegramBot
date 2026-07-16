namespace ContentPlatform.Abstractions;

/// <summary>
/// Süresi dolmakta olan sosyal hesap token'larını yenileyen port.
/// Platform bağlamı uygular; Worker (TokenRefreshJob) çağırır — derleme-zamanı bağımlılık olmadan.
/// </summary>
public interface ITokenRefresher
{
    /// <summary>Süresi yaklaşan token'ları yeniler. Yenilenen hesap sayısını döner.</summary>
    Task<int> RefreshDueAsync(CancellationToken ct);
}
