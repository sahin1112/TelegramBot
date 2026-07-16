using ContentPlatform.SharedKernel;

namespace ContentPlatform.Publishing.Domain;

/// <summary>Bir dış API çağrısının kullanım + maliyet kaydı.</summary>
public sealed class UsageRecord : Entity
{
    private UsageRecord() { }
    public UsageRecord(string provider, string operation, long units, decimal costUsd, IClock clock)
    {
        Provider = provider; Operation = operation; Units = units; CostUsd = costUsd; CreatedAt = clock.UtcNow;
    }
    public string Provider { get; private set; } = default!;   // openai, telegram, ...
    public string Operation { get; private set; } = default!;  // text, image, publish
    public long Units { get; private set; }                    // token / görsel / gönderi
    public decimal CostUsd { get; private set; }
}
