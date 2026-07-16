using ContentPlatform.SharedKernel;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Domain;

/// <summary>
/// Fiziksel yayın hedefi (grup/kanal/profil). Bir bot onlarca hedefe yayabilir.
/// Role: Editorial (normal), Test (deneme), AdminInbox (girdi — editoryal içerik ALMAZ).
/// CategoryId: hedefi bir kategoriye kapsar (null = tüm kategoriler).
/// </summary>
public sealed class PublicationTarget : Entity
{
    private PublicationTarget() { } // EF

    public PublicationTarget(
        Guid socialAccountId, PlatformKind platform, string externalTargetId, TargetType type,
        TargetRole role, Guid? categoryId, string title, string? language, string? timeZone,
        int? characterLimit, IClock clock)
    {
        SocialAccountId = socialAccountId;
        Platform = platform;
        ExternalTargetId = externalTargetId;
        Type = type;
        Role = role;
        CategoryId = categoryId;
        Title = title;
        Language = language;
        TimeZone = timeZone;
        CharacterLimit = characterLimit;
        IsActive = true;
        CreatedAt = clock.UtcNow;
    }

    public Guid SocialAccountId { get; private set; }
    public PlatformKind Platform { get; private set; }
    public string ExternalTargetId { get; private set; } = default!;  // ör. Telegram chatId -100...
    public TargetType Type { get; private set; }
    public TargetRole Role { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Language { get; private set; }
    public string? TimeZone { get; private set; }
    public int? CharacterLimit { get; private set; }
    public bool IsActive { get; private set; }

    public void Disable(IClock clock) { IsActive = false; Touch(clock); }
}
