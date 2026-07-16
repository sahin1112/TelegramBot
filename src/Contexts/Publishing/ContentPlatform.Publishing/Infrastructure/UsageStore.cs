using ContentPlatform.Abstractions;
using ContentPlatform.Publishing.Application;
using ContentPlatform.Publishing.Domain;
using ContentPlatform.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Publishing.Infrastructure;

internal sealed class UsageRecorder(PublishingDbContext db, IClock clock) : IUsageRecorder
{
    public async Task RecordAsync(string provider, string operation, long units, decimal costUsd, CancellationToken ct)
    {
        await db.UsageRecords.AddAsync(new UsageRecord(provider, operation, units, costUsd, clock), ct);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class UsageRepository(PublishingDbContext db) : IUsageRepository
{
    public async Task<IReadOnlyList<UsageRow>> GetSummaryAsync(DateTimeOffset since, CancellationToken ct) =>
        await db.UsageRecords.Where(x => x.CreatedAt >= since)
            .GroupBy(x => new { x.Provider, x.Operation })
            .Select(g => new UsageRow(g.Key.Provider, g.Key.Operation, g.Sum(x => x.Units), g.Sum(x => x.CostUsd)))
            .ToListAsync(ct);
}
