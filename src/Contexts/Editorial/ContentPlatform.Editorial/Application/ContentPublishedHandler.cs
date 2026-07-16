using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Editorial.Application;

/// <summary>Yayınlanan içeriği Published olarak işaretler.</summary>
public sealed class ContentPublishedHandler(IContentRepository repository, IClock clock)
    : IIntegrationEventHandler<ContentPublishedIntegrationEvent>
{
    public async Task HandleAsync(ContentPublishedIntegrationEvent e, CancellationToken ct)
    {
        var item = await repository.GetAsync(e.ContentItemId, ct);
        if (item is null) return;
        item.MarkPublished(clock);
        await repository.SaveChangesAsync(ct);
    }
}
