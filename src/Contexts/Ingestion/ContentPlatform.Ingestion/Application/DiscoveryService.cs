using System.Security.Cryptography;
using System.Text;
using ContentPlatform.Abstractions;
using ContentPlatform.Ingestion.Contracts;
using ContentPlatform.Ingestion.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ContentPlatform.Ingestion.Application;

/// <summary>Kaynakları tarar, yeni öğeleri dedup'lar, FactPack üretir ve keşif olayı yayınlar.</summary>
public sealed class DiscoveryService(
    ISourceRepository sources,
    IEnumerable<IFeedReader> readers,
    IDedupStore dedup,
    IFactPackExtractor factPack,
    IIntegrationEventPublisher bus,
    IClock clock,
    ILogger<DiscoveryService> logger)
{
    public async Task<int> DiscoverDueAsync(CancellationToken ct)
    {
        var due = await sources.ListDueAsync(clock.UtcNow, ct);
        var discovered = 0;

        foreach (var source in due)
        {
            var reader = readers.FirstOrDefault(r => r.CanRead(source.Type));
            if (reader is null) continue;

            string? lastHash = null;
            try
            {
                var items = await reader.ReadAsync(source, ct);
                foreach (var item in items)
                {
                    var hash = ComputeHash(item.Url, item.Title);
                    lastHash ??= hash;
                    if (await dedup.HasSeenAsync(hash, ct)) continue;

                    var fp = factPack.Extract(source, item);
                    var raw = item.Summary ?? item.Title;

                    await bus.PublishAsync(new ContentDiscoveredIntegrationEvent(
                        Guid.NewGuid(), clock.UtcNow, source.CategoryId, source.Type.ToString(),
                        item.Url, hash, item.Title, item.Summary, raw, fp), ct);

                    await dedup.MarkSeenAsync(hash, source.Id, ct);
                    discovered++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kaynak taranamadı: {SourceId} {Url}", source.Id, source.Url);
            }

            source.MarkPolled(lastHash, clock);
        }

        await sources.SaveChangesAsync(ct);
        if (discovered > 0) logger.LogInformation("{Count} yeni içerik keşfedildi.", discovered);
        return discovered;
    }

    private static string ComputeHash(string? url, string title)
    {
        var normalized = $"{url?.Trim().ToLowerInvariant()}|{title.Trim().ToLowerInvariant()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }
}
