using ContentPlatform.SharedKernel;

namespace ContentPlatform.Abstractions;

public enum Channel { Blog, Telegram, X, Instagram, Threads }
public enum Platform { Telegram, X, Instagram, Threads }

/// <summary>Çözülmüş (decrypt edilmiş) hesap kimliği — adaptöre CAĞRIDA verilir, global config'ten değil.</summary>
public sealed record AccountCredentials(Platform Platform, IReadOnlyDictionary<string, string> Values);

public sealed record MediaContent(byte[] Bytes, string ContentType, string FileName);

public sealed record PublishRequest(
    Channel Channel,
    string? Title,
    string Text,
    IReadOnlyList<string> Hashtags,
    string? MediaUrl,
    string? Link,
    string TargetRef,
    MediaContent? Media = null);

public sealed record PublishResult(bool Published, string? ExternalId, Error? Error);

/// <summary>Her yayın kanalı bir adaptör. Yeni kanal = yeni IChannelPublisher; çekirdek değişmez.</summary>
public interface IChannelPublisher
{
    Channel Channel { get; }
    Task<PublishResult> PublishAsync(PublishRequest request, AccountCredentials credentials, CancellationToken ct);
}
