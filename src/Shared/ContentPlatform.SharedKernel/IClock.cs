namespace ContentPlatform.SharedKernel;

/// <summary>Zaman soyutlaması (test edilebilirlik için).</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
