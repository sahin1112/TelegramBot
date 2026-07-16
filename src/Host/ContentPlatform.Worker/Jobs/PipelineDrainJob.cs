using ContentPlatform.Editorial.Application;
using Quartz;

namespace ContentPlatform.Worker.Jobs;

/// <summary>
/// Onaylanan içerikleri ilerletir: AI metin üretir → görsel kararı → yayına-hazır olayı.
/// Olay, Publishing tarafından hedeflere (Editorial/Test rolleri; AdminInbox hariç) dağıtılır.
/// </summary>
[DisallowConcurrentExecution]
public sealed class PipelineDrainJob(ContentGenerationService generation, ILogger<PipelineDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var count = await generation.GenerateForApprovedAsync(context.CancellationToken);
        if (count > 0) logger.LogInformation("PipelineDrainJob: {Count} içerik üretildi.", count);
    }
}
