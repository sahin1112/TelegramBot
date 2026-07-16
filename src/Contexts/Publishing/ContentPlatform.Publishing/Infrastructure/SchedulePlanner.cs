using ContentPlatform.Abstractions;
using ContentPlatform.Publishing.Application;
using ContentPlatform.SharedKernel;

namespace ContentPlatform.Publishing.Infrastructure;

/// <summary>
/// Kategori politikası + mevcut planlı yayınlara bakarak bir sonraki yayın anını belirler.
/// Saf hesap <see cref="SlotCalculator"/>'da; burada yalnızca veriyi toplar.
/// </summary>
internal sealed class SchedulePlanner(
    ISchedulePolicyProvider policies,
    IPublicationRepository publications,
    IClock clock) : ISchedulePlanner
{
    public async Task<DateTimeOffset?> NextSlotAsync(Guid? categoryId, CancellationToken ct)
    {
        var policy = await policies.GetForCategoryAsync(categoryId, ct);
        if (policy is null || policy.Mode == ScheduleMode.Immediate) return null;

        var existing = await publications.GetScheduledTimesForCategoryAsync(categoryId, ct);
        return SlotCalculator.Next(policy, clock.UtcNow, existing);
    }
}
