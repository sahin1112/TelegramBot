using ContentPlatform.SharedKernel;

namespace ContentPlatform.Abstractions;

public enum Channel { Blog, Telegram, X, Instagram, Threads, Youtube, TikTok }
public enum Platform { Telegram, X, Instagram, Threads, Youtube, TikTok }

/// <summary>Çözülmüş (decrypt edilmiş) hesap kimliği — adaptöre CAĞRIDA verilir, global config'ten değil.
/// SocialAccountId: adaptör token yenilediğinde kalıcı kaydı güncelleyebilsin diye (ICredentialUpdater).</summary>
public sealed record AccountCredentials(Platform Platform, IReadOnlyDictionary<string, string> Values, Guid SocialAccountId = default);

/// <summary>
/// Adaptörlerin YENİLENEN token'ları kalıcı kayda geri yazma kapısı (Platform bağlamı uygular).
/// X (OAuth2) gibi platformlarda refresh token TEK KULLANIMLIKTIR — her yenilemede yenisi verilir;
/// kalıcılaştırılmazsa bir sonraki yenileme BAŞARISIZ olur ve hesap bağlantısı kopar.
/// </summary>
public interface ICredentialUpdater
{
    Task UpdateAsync(Guid socialAccountId, IReadOnlyDictionary<string, string> values, DateTimeOffset? tokenExpiresAt, CancellationToken ct);
}

public sealed record MediaContent(byte[] Bytes, string ContentType, string FileName);

public sealed record PublishRequest(
    Channel Channel,
    string? Title,
    string Text,
    IReadOnlyList<string> Hashtags,
    string? MediaUrl,
    string? Link,
    string TargetRef,
    MediaContent? Media = null,
    string? ButtonUrl = null,
    string? ButtonText = null,
    string? VideoUrl = null,
    MediaContent? VideoMedia = null);

public sealed record PublishResult(bool Published, string? ExternalId, Error? Error);

/// <summary>Her yayın kanalı bir adaptör. Yeni kanal = yeni IChannelPublisher; çekirdek değişmez.</summary>
public interface IChannelPublisher
{
    Channel Channel { get; }
    Task<PublishResult> PublishAsync(PublishRequest request, AccountCredentials credentials, CancellationToken ct);
}
