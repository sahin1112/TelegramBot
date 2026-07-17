using ContentPlatform.SharedKernel;

namespace ContentPlatform.Site.Domain;

/// <summary>Blog yorumu. Varsayılan Pending → admin onayı sonrası görünür (00 §5.12, §13).</summary>
public sealed class Comment : Entity
{
    private Comment() { } // EF

    public Comment(Guid blogPostId, string authorName, string? authorEmail, string body, CommentStatus status, string ipHash, IClock clock)
    {
        BlogPostId = blogPostId;
        AuthorName = authorName;
        AuthorEmail = authorEmail;
        Body = body;
        Status = status;
        IpHash = ipHash;
        CreatedAt = clock.UtcNow;
    }

    public Guid BlogPostId { get; private set; }
    public string AuthorName { get; private set; } = default!;
    public string? AuthorEmail { get; private set; }
    public string Body { get; private set; } = default!;          // düz metin (etiketler ayıklanmış)
    public CommentStatus Status { get; private set; }
    public string IpHash { get; private set; } = default!;         // KVKK: IP ham değil, hash saklanır
    public DateTimeOffset? ModeratedAt { get; private set; }

    public void Moderate(CommentStatus status, IClock clock)
    {
        Status = status;
        ModeratedAt = clock.UtcNow;
        Touch(clock);
    }
}

public enum CommentStatus { Pending, Approved, Rejected, Spam }
