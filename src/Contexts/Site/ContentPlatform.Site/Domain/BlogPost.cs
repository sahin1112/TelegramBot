using ContentPlatform.SharedKernel;

namespace ContentPlatform.Site.Domain;

/// <summary>
/// Blogun kanonik yayın birimi. Editorial içeriği "yayına hazır" olduğunda üretilir/güncellenir.
/// İçerik kimliğine göre tekildir (idempotent): aynı içerik yeniden üretilirse gönderi güncellenir.
/// </summary>
public sealed class BlogPost : Entity
{
    private BlogPost() { } // EF

    public BlogPost(
        Guid contentItemId,
        Guid? categoryId,
        string slug,
        string title,
        string metaDescription,
        string bodyHtml,
        string? coverImageUrl,
        string? coverImageAlt,
        IReadOnlyList<string> tags,
        IClock clock)
    {
        ContentItemId = contentItemId;
        CategoryId = categoryId;
        Slug = slug;
        Title = title;
        MetaDescription = metaDescription;
        BodyHtml = bodyHtml;
        CoverImageUrl = coverImageUrl;
        CoverImageAlt = coverImageAlt;
        Tags = tags.ToList();
        PublishedAt = clock.UtcNow;
        CreatedAt = clock.UtcNow;
        CommentsEnabled = true;
    }

    public Guid ContentItemId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Slug { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string MetaDescription { get; private set; } = default!;
    public string BodyHtml { get; private set; } = default!;
    public string? CoverImageUrl { get; private set; }
    public string? CoverImageAlt { get; private set; }
    public List<string> Tags { get; private set; } = new();
    public DateTimeOffset PublishedAt { get; private set; }
    public long Views { get; private set; }
    public bool CommentsEnabled { get; private set; }

    /// <summary>İçerik yeniden üretildiğinde gövde/başlık/görsel güncellenir; slug ve yayın tarihi korunur.</summary>
    public void Update(string title, string metaDescription, string bodyHtml, string? coverImageUrl, string? coverImageAlt, IReadOnlyList<string> tags, IClock clock)
    {
        Title = title;
        MetaDescription = metaDescription;
        BodyHtml = bodyHtml;
        if (!string.IsNullOrEmpty(coverImageUrl)) CoverImageUrl = coverImageUrl;
        CoverImageAlt = coverImageAlt;
        Tags = tags.ToList();
        Touch(clock);
    }

    public void RegisterView() => Views++;
}
