using ContentPlatform.Abstractions;
using Quartz;

namespace ContentPlatform.Worker.Jobs;

/// <summary>Süresi yaklaşan sosyal token'ları yeniler. İş mantığı Platform bağlamındaki ITokenRefresher'da.</summary>
[DisallowConcurrentExecution]
public sealed class TokenRefreshJob(ITokenRefresher refresher, ILogger<TokenRefreshJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var count = await refresher.RefreshDueAsync(context.CancellationToken);
        logger.LogInformation("TokenRefreshJob: {Count} hesap işlendi.", count);
    }
}
