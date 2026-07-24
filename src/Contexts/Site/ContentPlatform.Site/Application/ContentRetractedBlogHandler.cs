using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Contracts;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Site.Application;

/// <summary>İçerik silinince public blog gönderisini kaldırır (idempotent — yoksa sessiz geçer).</summary>
public sealed class ContentRetractedBlogHandler(IBlogRepository repository, ILogger<ContentRetractedBlogHandler> logger)
    : IIntegrationEventHandler<ContentRetractedIntegrationEvent>
{
    public async Task HandleAsync(ContentRetractedIntegrationEvent e, CancellationToken ct)
    {
        var post = await repository.GetByContentItemAsync(e.ContentItemId, ct);
        if (post is null) return;
        repository.Remove(post);
        await repository.SaveChangesAsync(ct);
        logger.LogInformation("Blog gönderisi kaldırıldı (içerik silindi): {Slug}", post.Slug);
    }
}
