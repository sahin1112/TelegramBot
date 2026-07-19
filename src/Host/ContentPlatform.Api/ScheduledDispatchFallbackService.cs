using ContentPlatform.Publishing.Application;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Api;

/// <summary>
/// YEDEK planlı-yayın göndericisi (Api süreci içinde). Asıl gönderici Worker'daki ScheduledDispatchJob'dır;
/// ancak Worker servisi (TelegramWorker) durmuş ya da eski sürümde kalmışsa planlı yayınlar süresiz
/// bekliyordu ("saati geldi ama gitmedi" — 'Şimdi gönder' Api'de çalıştığı için o çalışıyordu).
/// Bu servis her dakika zamanı gelmiş planlı yayınlara bakar; her yayını göndermeden önce
/// TryClaimScheduledAsync ile ATOMİK sahiplenir → Worker ile aynı anda çalışsa bile çifte gönderim OLMAZ
/// (DB'de Scheduled→Pending geçişini yalnız bir süreç kazanır).
///
/// Bu yedek devreye girdiyse loglara UYARI düşer: Worker'ın çalışıp çalışmadığını kontrol edin —
/// asıl yol Worker'dır (IIS uygulama havuzu boşta geri dönüştürülebilir; yedek o aralıkta koşmaz).
/// </summary>
internal sealed class ScheduledDispatchFallbackService(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledDispatchFallbackService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Uygulama tam ayağa kalksın (migration vb.) diye ilk turdan önce kısa bekleme.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var publications = scope.ServiceProvider.GetRequiredService<IPublicationRepository>();
                var distribution = scope.ServiceProvider.GetRequiredService<DistributionService>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();

                var due = await publications.GetDueScheduledAsync(clock.UtcNow, 50, stoppingToken);
                var ok = 0; var claimed = 0;
                foreach (var pub in due)
                {
                    // Sahiplenemedik → Worker (asıl gönderici) almış demektir; karışma.
                    if (!await publications.TryClaimScheduledAsync(pub.Id, stoppingToken)) continue;
                    claimed++;
                    if (await distribution.PublishOneAsync(pub, stoppingToken)) ok++;
                    await publications.SaveChangesAsync(stoppingToken);
                }

                if (claimed > 0)
                    logger.LogWarning(
                        "Planlı yayınları YEDEK gönderici (Api) gönderdi: {Ok}/{Claimed}. " +
                        "Asıl gönderici Worker'dır — TelegramWorker servisinin çalıştığını ve güncel olduğunu kontrol edin.",
                        ok, claimed);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Tek turun hatası döngüyü öldürmesin; sonraki dakika yeniden denenir.
                logger.LogError(ex, "Yedek planlı gönderici (Api) hata verdi; sonraki dakika yeniden denenecek.");
            }

            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }
        }
    }
}
