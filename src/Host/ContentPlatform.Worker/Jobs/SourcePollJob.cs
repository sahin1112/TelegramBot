using ContentPlatform.Ingestion.Application;
using Quartz;

namespace ContentPlatform.Worker.Jobs;

/// <summary>Zamanı gelen kaynakları tarar: RSS/sayfa → dedup → FactPack → keşif olayı → onay kuyruğu.</summary>
[DisallowConcurrentExecution]
public sealed class SourcePollJob(DiscoveryService discovery, ILogger<SourcePollJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var count = await discovery.DiscoverDueAsync(context.CancellationToken);
        logger.LogInformation("SourcePollJob: {Count} yeni içerik keşfedildi.", count);
    }
}
