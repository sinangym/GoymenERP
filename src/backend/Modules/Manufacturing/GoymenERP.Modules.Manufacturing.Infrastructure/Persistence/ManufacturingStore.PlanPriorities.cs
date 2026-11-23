using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingStore : IManufacturingPlanPriorityStore
{
    public async Task<PlanPriorityViewV1?> FindPlanPriorityAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        var plan = await dbContext.Plans.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == companyId && row.Id == planId, cancellationToken);
        if (plan is null) return null;
        var row = await dbContext.PlanPriorityVersions.AsNoTracking().Where(row => row.CompanyId == companyId && row.PlanId == planId)
            .OrderByDescending(row => row.Revision).FirstOrDefaultAsync(cancellationToken);
        return PriorityView(companyId, planId, plan.Version, row);
    }

    public async Task<PlanPriorityViewV1> ChangePlanPriorityAsync(Guid planId, long expectedVersion, long expectedPriorityRevision,
        int score, string reason, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata); var companyId = RequireResolvedCompany();
        var plan = await dbContext.Plans.SingleOrDefaultAsync(row => row.CompanyId == companyId && row.Id == planId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.PlanNotFound, "Bu şirketin böyle bir planı yok.");
        if (plan.Version != expectedVersion) throw new ManufacturingVersionConflictException(expectedVersion, plan.Version);
        if (plan.Status == (int)PlanStatus.Cancelled)
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanCancelled, "İptal edilmiş planın önceliği değiştirilemez.");
        var current = await dbContext.PlanPriorityVersions.AsNoTracking().Where(row => row.CompanyId == companyId && row.PlanId == planId)
            .OrderByDescending(row => row.Revision).FirstOrDefaultAsync(cancellationToken);
        var before = PriorityView(companyId, planId, plan.Version, current);
        var next = new ProductionPlanPriorityV1(before.PriorityScore, before.PriorityRevision).Change(expectedPriorityRevision, score, reason);
        var now = timeProvider.GetUtcNow();
        var added = new PlanPriorityVersionRow { CompanyId = companyId, PlanId = planId, Revision = next.Revision,
            Score = next.Score, RecordedAtUtc = now, RecordedBy = metadata.ActorId.Trim(), Reason = reason.Trim(),
            CorrelationId = metadata.CorrelationId.Trim() };
        dbContext.PlanPriorityVersions.Add(added);
        // Participate in the existing plan Version fence without rewriting plan content or its approval version.
        // Mark explicitly: a fixed clock must not let EF optimize away the concurrency-checking parent UPDATE.
        plan.UpdatedAtUtc = now;
        dbContext.Entry(plan).Property(row => row.UpdatedAtUtc).IsModified = true;
        var after = PriorityView(companyId, planId, plan.Version, added);
        Audit(metadata, companyId, "Manufacturing.PlanPriority", planId, "manufacturing.plan.priority-changed.v1",
            "ChangePriority", before.PriorityRevision, after.PriorityRevision,
            JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(after, Json), reason.Trim());
        Complete(metadata, companyId, after);
        await SaveAsync(cancellationToken);
        return after;
    }

    private static PlanPriorityViewV1 PriorityView(Guid companyId, Guid planId, long planVersion, PlanPriorityVersionRow? row) =>
        new(companyId, planId, planVersion, row?.Score ?? 0, row?.Revision ?? 0, row?.RecordedBy, row?.RecordedAtUtc, row?.Reason);
}

internal sealed class PlanPriorityVersionRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid PlanId { get; set; }
    public long Revision { get; set; }
    public int Score { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}
