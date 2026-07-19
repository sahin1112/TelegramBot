using ContentPlatform.Publishing.Application;
using ContentPlatform.SharedKernel;
using Quartz;

namespace ContentPlatform.Worker.Jobs;

/// <summary>
/// Zamanı gelen planlı yayınları gönderir (Status=Scheduled &amp; ScheduledAt ≤ now).
/// Payload snapshot'tan gönderilir; AI tekrar çalışmaz. Kategori kadansına göre planlananlar buradan çıkar.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ScheduledDispatchJob(
    IPublicationRepository publications,
    DistributionService distribution,
    IClock clock,
    ILogger<ScheduledDispatchJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var due = await publications.GetDueScheduledAsync(clock.UtcNow, 50, ct);
        var ok = 0;
        foreach (var pub in due)
        {
            // Atomik sahiplenme: Api'deki yedek gönderici aynı anda çalışıyorsa çifte gönderim olmasın.
            if (!await publications.TryClaimScheduledAsync(pub.Id, ct)) continue;
            if (await distribution.PublishOneAsync(pub, ct)) ok++;
            await publications.SaveChangesAsync(ct);
        }
        if (due.Count > 0) logger.LogInformation("ScheduledDispatchJob: {Ok}/{Total} planlı yayın gönderildi.", ok, due.Count);
    }
}
