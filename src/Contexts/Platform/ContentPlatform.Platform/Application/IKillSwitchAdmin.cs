using ContentPlatform.Platform.Domain;

namespace ContentPlatform.Platform.Application;

/// <summary>Kill-switch yönetimi (admin paneli). Okuma tarafı IKillSwitch (Abstractions) ile ayrık.</summary>
public interface IKillSwitchAdmin
{
    Task<IReadOnlyList<KillSwitchDto>> ListAsync(CancellationToken ct);
    Task SetAsync(KillScope scope, string? key, bool engaged, string? reason, CancellationToken ct);
}

public sealed record KillSwitchDto(KillScope Scope, string? Key, bool Engaged, string? Reason, DateTimeOffset At);
public sealed record SetKillSwitchRequest(KillScope Scope, string? Key, bool Engaged, string? Reason);
