using ContentPlatform.Publishing.Application;
using ContentPlatform.SharedKernel;
using Quartz;

namespace ContentPlatform.Worker.Jobs;

/// <summary>
/// KURTARMA işi: gönderim sırasında süreç kesilmiş/çökmüşse Pending'de TAKILI kalan yayınları bulur
/// ve 1 dk sonrasına yeniden planlar (ScheduledDispatch gönderir). Eşik 10 dk — en uzun adaptör
/// beklemesi (~3 dk video işleme) fazlasıyla içinde kalır, aktif gönderime DOKUNMAZ.
///
/// NOT: Başarısız yayınların yeniden denemesi artık burada DEĞİL. Her hata, yalnız o hedefi artan
/// gecikmeyle (1-2-5-10 dk) yeniden planlar (Publication.MarkFailedWithRetry); toplam 5 hak bitince
/// kalıcı Failed olur ve panelden elle "Yeniden dene" ile gönderilir. Böylece geçici platform
/// sorunlarında 30 sn'de bir ard arda vurup hakları dakikada tüketme davranışı kalktı.
/// </summary>
[DisallowConcurrentExecution]
public sealed class OutboxDispatchJob(
    IPublicationRepository publications,
    IClock clock,
    ILogger<OutboxDispatchJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var stuck = await publications.GetStuckPendingAsync(clock.UtcNow.AddMinutes(-10), 50, ct);
        foreach (var pub in stuck)
        {
            pub.Reschedule(clock.UtcNow.AddMinutes(1), clock);
            logger.LogWarning("Takılı yayın kurtarıldı (Pending → yeniden planlandı): kanal={Channel} hedef={Target} id={Id}",
                pub.Channel, pub.TargetRef, pub.Id);
        }
        if (stuck.Count > 0) await publications.SaveChangesAsync(ct);
    }
}
