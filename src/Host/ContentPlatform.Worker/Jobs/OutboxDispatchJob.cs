using ContentPlatform.Publishing.Application;
using Quartz;

namespace ContentPlatform.Worker.Jobs;

/// <summary>Başarısız yayınları yeniden dener (maxAttempts'a kadar). Payload snapshot'tan; AI tekrar çalışmaz.</summary>
[DisallowConcurrentExecution]
public sealed class OutboxDispatchJob(
    IPublicationRepository publications,
    DistributionService distribution,
    ILogger<OutboxDispatchJob> logger) : IJob
{
    private const int MaxAttempts = 3;

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var retriable = await publications.GetRetriableAsync(MaxAttempts, 50, ct);
        var ok = 0;
        foreach (var pub in retriable)
        {
            if (await distribution.PublishOneAsync(pub, ct)) ok++;
            await publications.SaveChangesAsync(ct);
        }
        if (retriable.Count > 0) logger.LogInformation("OutboxDispatchJob: {Ok}/{Total} yeniden denendi.", ok, retriable.Count);
    }
}
