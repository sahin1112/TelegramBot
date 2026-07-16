using ContentPlatform.Abstractions;
using ContentPlatform.Publishing.Domain;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Publishing.Application;

public sealed record UsageRow(string Provider, string Operation, long Units, decimal CostUsd);
public sealed record UsageDashboard(int Days, IReadOnlyList<UsageRow> Rows, decimal TotalUsd, decimal UsdToTry, decimal TotalTry);

public interface IUsageRepository
{
    Task<IReadOnlyList<UsageRow>> GetSummaryAsync(DateTimeOffset since, CancellationToken ct);
}

/// <summary>Kullanım panosu: hangi API ne kadar + toplam $ ve ₺ (kur ayarlardan).</summary>
public sealed class UsageService(IUsageRepository repository, ISettingsProvider settings, IClock clock)
{
    public async Task<UsageDashboard> GetAsync(int days, CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-Math.Max(1, days));
        var rows = await repository.GetSummaryAsync(since, ct);
        var totalUsd = rows.Sum(r => r.CostUsd);
        var rate = decimal.TryParse(await settings.GetAsync("Pricing:UsdToTry", ct), out var r) ? r : 0m;
        return new UsageDashboard(days, rows, totalUsd, rate, totalUsd * rate);
    }
}
