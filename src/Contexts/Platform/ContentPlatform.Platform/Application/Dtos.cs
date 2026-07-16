using ContentPlatform.Platform.Domain;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Platform.Application;

public sealed record CreateSocialAccountRequest(
    PlatformKind Platform,
    string DisplayName,
    Dictionary<string, string> Credentials,   // platforma göre alanlar (ör. X: 4 anahtar; IG: token)
    DateTimeOffset? TokenExpiresAt,
    Guid? SiteId);

public sealed record AddTargetRequest(
    string ExternalTargetId, TargetType Type, TargetRole Role, Guid? CategoryId, string Title,
    string? Language, string? TimeZone, int? CharacterLimit);

/// <summary>Token/kimlik ASLA düz dönmez — maskeli özet.</summary>
public sealed record SocialAccountDto(
    Guid Id, PlatformKind Platform, string DisplayName, AccountStatus Status,
    DateTimeOffset? TokenExpiresAt, string? LastError, int TargetCount);

public sealed record TargetDto(
    Guid Id, PlatformKind Platform, string ExternalTargetId, TargetType Type, TargetRole Role,
    Guid? CategoryId, string Title, bool IsActive);

public sealed record SocialAccountDetailDto(
    Guid Id, PlatformKind Platform, string DisplayName, AccountStatus Status,
    DateTimeOffset? TokenExpiresAt, string? LastError, IReadOnlyList<TargetDto> Targets);
