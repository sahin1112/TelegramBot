using ContentPlatform.Editorial.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace ContentPlatform.Worker.Jobs;

/// <summary>
/// RSS otomasyonuyla işaretlenmiş TASLAK içerikleri otomatik hazırlar (metin + görsel + video).
/// Aynı anda birden çok içerik gelebildiği için sınırlı PARALELLİKLE işler; her içerik AYRI scope
/// (DbContext) kullanır çünkü EF DbContext thread-safe değildir. Adım başına 3 deneme; sonra "üretilemedi".
/// </summary>
[DisallowConcurrentExecution]
public sealed class AutoDraftJob(IServiceScopeFactory scopeFactory, ILogger<AutoDraftJob> logger) : IJob
{
    private const int BatchSize = 20;
    private const int MaxParallel = 3;

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        // Adayları TEK scope'ta çek (yalnız Id).
        List<Guid> ids;
        using (var scope = scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IContentRepository>();
            ids = (await repo.GetAutoDraftCandidateIdsAsync(BatchSize, ct)).ToList();
        }
        if (ids.Count == 0) return;

        var processed = 0;
        await Parallel.ForEachAsync(ids,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = ct },
            async (id, token) =>
            {
                // Her içerik kendi scope'unda (kendi DbContext'i) — paralel güvenli.
                using var scope = scopeFactory.CreateScope();
                var gen = scope.ServiceProvider.GetRequiredService<ContentGenerationService>();
                try
                {
                    if (await gen.ProcessAutoDraftAsync(id, token)) Interlocked.Increment(ref processed);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "AutoDraftJob içerik işlenemedi: {Id}", id);
                }
            });

        if (processed > 0) logger.LogInformation("AutoDraftJob: {Count} içerik otomatik hazırlandı/ilerletildi.", processed);
    }
}
