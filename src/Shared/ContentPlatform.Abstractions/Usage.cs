namespace ContentPlatform.Abstractions;

/// <summary>Dış API kullanımını ve maliyetini kaydeder (hangi API ne kadar / kaç $).</summary>
public interface IUsageRecorder
{
    Task RecordAsync(string provider, string operation, long units, decimal costUsd, CancellationToken ct);
}
