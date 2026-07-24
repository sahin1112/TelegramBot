using ContentPlatform.Abstractions;
using ContentPlatform.Platform.Application;

namespace ContentPlatform.Platform.Infrastructure;

/// <summary>
/// Kategori otomasyon + görsel havuzu ayarlarını bağlamlar arasına açar (Editorial keşifte çözer).
/// </summary>
internal sealed class CategoryAutomationProvider(ICategoryRepository categories) : ICategoryAutomationProvider
{
    public async Task<CategoryAutomation?> GetAsync(Guid categoryId, CancellationToken ct)
    {
        var c = await categories.GetAsync(categoryId, ct);
        return c is null ? null : new CategoryAutomation(
            c.AutoContent, c.AutoImage, c.AutoVideo, c.AutoPublish,
            c.Card1x1, c.CardReels, c.AttentionBadges);
    }
}
