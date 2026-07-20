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
            try
            {
                if (await distribution.PublishOneAsync(pub, ct)) ok++;
                await publications.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // TEK yayının hatası turu ÖLDÜRMESİN; sahiplenilen kayıt Pending'de takılı kalmasın
                // diye 1 dk sonraya yeniden planlanır (kayıt iptalden etkilenmesin: None ile yazılır).
                logger.LogError(ex, "Planlı yayın gönderilemedi (id={Id}); 1 dk sonraya yeniden planlandı.", pub.Id);
                try { pub.Reschedule(clock.UtcNow.AddMinutes(1), clock); await publications.SaveChangesAsync(CancellationToken.None); }
                catch (Exception ex2) { logger.LogError(ex2, "Planlı yayın kurtarma kaydı başarısız (id={Id}).", pub.Id); }
            }
        }
        if (due.Count > 0) logger.LogInformation("ScheduledDispatchJob: {Ok}/{Total} planlı yayın gönderildi.", ok, due.Count);
    }
}
