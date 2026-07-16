namespace ContentPlatform.SharedKernel;

/// <summary>Guid kimlikli temel varlık. Domain saftır; altyapı bağımlılığı taşımaz.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset? UpdatedAt { get; protected set; }

    protected void Touch(IClock clock) => UpdatedAt = clock.UtcNow;
}
