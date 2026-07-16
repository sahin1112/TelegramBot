using ContentPlatform.SharedKernel;

namespace ContentPlatform.Editorial.Domain;

/// <summary>
/// İçeriğin bir sürümü. AI ve manuel içerik AYNI yapıyı kullanır (içerik ile AI üretimi yapışık değil).
/// Düzenleme yeni bir revizyon oluşturur; geçmiş korunur.
/// </summary>
public sealed class ContentRevision : Entity
{
    private ContentRevision() { } // EF

    public ContentRevision(
        Guid contentItemId,
        int revisionNumber,
        string title,
        string shortX,
        string bodyHtml,
        string? instagramCaption,
        IReadOnlyList<string> tags,
        string? primaryKeyword,
        string? imageAltText,
        string createdBy,
        IClock clock)
    {
        ContentItemId = contentItemId;
        RevisionNumber = revisionNumber;
        Title = title;
        ShortX = shortX;
        BodyHtml = bodyHtml;
        InstagramCaption = instagramCaption;
        Tags = tags.ToList();
        PrimaryKeyword = primaryKeyword;
        ImageAltText = imageAltText;
        CreatedBy = createdBy;
        IsCurrent = true;
        CreatedAt = clock.UtcNow;
    }

    public Guid ContentItemId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Title { get; private set; } = default!;
    public string ShortX { get; private set; } = default!;            // X'e uygun (<=280)
    public string BodyHtml { get; private set; } = default!;          // ana makale / blog gövdesi
    public string? InstagramCaption { get; private set; }            // IG'ye uygun (<=2200)
    public List<string> Tags { get; private set; } = new();
    public string? PrimaryKeyword { get; private set; }
    public string? ImageAltText { get; private set; }
    public string CreatedBy { get; private set; } = default!;
    public bool IsCurrent { get; private set; }

    internal void Supersede() => IsCurrent = false;
}
