using System.Globalization;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

// Üretim F-010a (URT-D29): the termin (due date) is entered by hand on the plan. The system never computes it from line
// speed, a calendar or capacity (capacity and calendar are outside v1). It lives on the plan (a reversible implementation
// detail of URT-D29) and the plan's order reads it from there, so it is recorded once.

/// <summary>
/// Üretim F-010a (URT-D29): the plan's termin in one save — the plan row's termin column and termin revision, the audit
/// event and the command record — or nothing. A port of its own beside <see cref="IManufacturingPlanLifecycleStore"/>; the one
/// store implements it. The domain decides on the plan rebuilt from storage (with its order's status).
/// </summary>
public interface IManufacturingPlanDueDateStore
{
    /// <summary>Enters, changes or (with a null <paramref name="dueDate"/>) removes the plan's termin.</summary>
    Task<ProductionPlan> ChangeDueDateAsync(Guid planId, long expectedVersion, long expectedDueDateRevision,
        DateOnly? dueDate, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>POST /plans/{planId}/due-date</c>: the plan version and termin revision the caller read, and the termin to keep — a
/// calendar day (<c>yyyy-MM-dd</c>, no time) — or null to remove it.
/// </summary>
public sealed record ChangePlanDueDateRequestV1(
    long ExpectedVersion,
    long ExpectedDueDateRevision,
    DateOnly? RequestedDueDate);

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// <c>POST /plans/{planId}/due-date</c> (F-010a, URT-D29) under <c>manufacturing.plans.write</c>: enters, changes or
    /// removes the plan's hand-entered termin. The plan's version does not move; its termin revision does. A cancelled plan,
    /// a closed order and no change are 422; a moved termin revision is 409. No date rule is applied (whether a past day is
    /// refused is not decided). ADR-011 as the other plan commands: the same key replays the first answer.
    /// </summary>
    public Task<ManufacturingApiResult<PlanViewV1>> ChangePlanDueDateAsync(Guid planId,
        ChangePlanDueDateRequestV1? request, string idempotencyKey, string permission,
        CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedVersion"));
        }

        if (request.ExpectedDueDateRevision < 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedDueDateRevision"));
        }

        // The type's default day (0001-01-01) is no entered termin but an unset value; the screen refuses it as well.
        if (request.RequestedDueDate == DateOnly.MinValue)
        {
            return Task.FromResult(Invalid<PlanViewV1>("requestedDueDate"));
        }

        PlanParameterSnapshotV1? live = null;
        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.ChangePlanDueDate,
            new
            {
                planId,
                expectedVersion = request.ExpectedVersion,
                expectedDueDateRevision = request.ExpectedDueDateRevision,
                // The calendar day as its canonical text, so the hash never depends on a serializer's date settings.
                requestedDueDate = request.RequestedDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }, ManufacturingApiStatus.Ok,
            async (metadata, actor, companyId) =>
            {
                if (planDueDates is null)
                {
                    return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
                }

                // The Draft's answer uses the profile in force today, read once before the save (as the plan read).
                if (await plans.FindPlanAsync(planId, cancellationToken) is { ApprovedAtUtc: null } current)
                {
                    live = await ReadLiveParameterProfileAsync(current, cancellationToken);
                    if (await FenceCapturedAsync<PlanViewV1>(permission, companyId, actor, cancellationToken) is { } fenced)
                    {
                        return fenced;
                    }
                }

                var changed = await planDueDates.ChangeDueDateAsync(planId, request.ExpectedVersion,
                    request.ExpectedDueDateRevision, request.RequestedDueDate, metadata, cancellationToken);
                return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(changed, live), false,
                    null);
            }, stored => PlanView((ProductionPlan)stored, live), cancellationToken);
    }
}
