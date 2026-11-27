using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;

namespace GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the proposed over-plan shipment exceptions (SAT-D417) waiting for approval, as Logistics lists
/// them for the Work inbox. An exception waits while it is Pending (status 1, version 1) on an Issued instruction — the state
/// <c>ApproveLogisticsPlanExcessV1</c> accepts — and is decided on the plan-excess approval screen by a holder of
/// <c>logistics.shipments.plan-excess.approve</c> (the inbox decides nothing). Every pending exception of the company is
/// returned, the proposer's own included: Work hides a maker's own item. WP6: the owner's approval refuses the proposer
/// (ROL-D14, <c>logistics_plan_excess_self_approval</c>), so the proposer is exactly the owner's "may not decide" set.
/// <para>
/// The rule results come only from stored values: the line's instructed (planned) quantity and the proposed excess quantity
/// (no threshold exists: any positive excess needs approval). The unit is <c>kg</c> on a kilogram line; Logistics stores only
/// the unit identity of other lines, so their unit is left empty. A blocked line is listed too (the approval waits until the
/// block is lifted) and says so in its own rule.
/// </para>
/// </summary>
internal sealed class LogisticsPlanExcessWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public const string ModuleName = "logistics";
    public const string KindName = "logistics.plan-excess.v1";

    /// <summary>The plan-excess approval screen (L-PLAN-EXCESS-APPROVE), where the approve button lives.</summary>
    public const string ScreenPath = "/lojistik/plan-ustu-sevk-onay";

    /// <summary>A quantity above the instructed (planned) quantity was proposed: threshold = instructed, deviation = the excess.</summary>
    public const string ExcessRule = "logistics.plan-excess-quantity";

    /// <summary>The line already carries approved excess: actual = the approved excess so far.</summary>
    public const string AlreadyApprovedRule = "logistics.plan-excess-already-approved";

    /// <summary>The line is blocked: approval is refused until the block is lifted.</summary>
    public const string LineBlockedRule = "logistics.plan-excess-line-blocked";

    /// <summary>The proposal's first and only version (the state check: Pending is version 1).</summary>
    private const long PendingVersion = 1;
    private const int PendingStatus = 1;
    private const string KilogramUnit = "kg";

    private readonly LogisticsWorkInboxRows rows;
    private readonly LogisticsWorkInboxFence fence;

    public LogisticsPlanExcessWorkInboxSourceV1(LogisticsDbContext dbContext, LogisticsWorkInboxFence fence)
        : this(new LogisticsWorkInboxDbRows(dbContext), fence)
    {
    }

    internal LogisticsPlanExcessWorkInboxSourceV1(LogisticsWorkInboxRows rows, LogisticsWorkInboxFence fence)
    {
        this.rows = rows;
        this.fence = fence;
    }

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => LogisticsPermissionManifestV1.ShipmentPlanExcessApprove;


    /// <summary>Purpose read over the exact company's exception, including completed, blocked or stale instructions.
    /// Native approval/replay decides whether the frozen versions are still usable. No mutable usage counter is a receipt.</summary>
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (itemId == Guid.Empty || await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        var query = from exception in rows.PlanExcessExceptions
            join instruction in rows.Instructions
                on new { exception.CompanyId, Id = exception.InstructionId } equals new { instruction.CompanyId, instruction.Id }
            join line in rows.Lines
                on new { exception.CompanyId, exception.InstructionId, exception.LineId }
                equals new { line.CompanyId, line.InstructionId, line.LineId }
            where exception.CompanyId == context.CompanyId && exception.ExceptionId == itemId
                && instruction.CompanyId == context.CompanyId && line.CompanyId == context.CompanyId
            select new { exception.ExceptionId, exception.InstructionId, exception.LineId, exception.Version, exception.Status,
                exception.Quantity, exception.Reason, exception.ProposedBy, exception.ProposedAtUtc,
                InstructionVersion = instruction.Version, InstructionStatus = instruction.Status,
                line.Instructed, line.ApprovedPlanExcess, line.UnitIsKilogram, line.IsBlocked };
        var found = await rows.ListAsync(query.Take(2), cancellationToken);
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        if (found.Count == 0) return null;
        if (found.Count != 1) throw new InvalidOperationException("Ambiguous native plan excess source.");
        var row = found[0];
        if (row.ExceptionId == Guid.Empty || row.InstructionId == Guid.Empty || row.LineId == Guid.Empty
            || !(row.Status == 1 && row.Version == 1 || row.Status == 2 && row.Version == 2)
            || row.InstructionVersion < 1 || !Enum.IsDefined((LoadingInstructionStatus)row.InstructionStatus)
            || row.Quantity <= 0 || row.Instructed < 0 || row.ApprovedPlanExcess < 0
            || string.IsNullOrWhiteSpace(row.Reason) || row.Reason.Trim() != row.Reason || row.Reason.Length > 500 || row.Reason.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(row.ProposedBy) || row.ProposedBy.Length > 128 || row.ProposedAtUtc == default)
            throw new InvalidOperationException("Malformed native plan excess source.");
        // Capture scalar metadata before the awaited final read, including for in-memory owner fixtures.
        var version = row.Version; var status = row.Status; var instructionVersion = row.InstructionVersion;
        var instructionStatus = row.InstructionStatus;
        var final = await rows.ListAsync(query.Select(value => new { value.Version, value.Status,
            value.InstructionVersion, value.InstructionStatus }).Take(2), cancellationToken);
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        if (final.Count != 1 || final[0].Version != version || final[0].Status != status
            || final[0].InstructionVersion != instructionVersion || final[0].InstructionStatus != instructionStatus)
            throw new InvalidOperationException("The native plan excess source changed during the read.");
        var unit = row.UnitIsKilogram ? KilogramUnit : null;
        var rules = new List<WorkApprovalRuleResultV1> { new(ExcessRule,
            WorkInboxRulesV1.Decimal(row.Instructed), null, WorkInboxRulesV1.Decimal(row.Quantity), unit) };
        if (row.ApprovedPlanExcess > 0) rules.Add(new(AlreadyApprovedRule, null, WorkInboxRulesV1.Decimal(row.ApprovedPlanExcess), null, unit));
        if (row.IsBlocked) rules.Add(new(LineBlockedRule, null, null, null, null));
        var item = new WorkInboxItemV1(ModuleName, KindName, row.ExceptionId, LogisticsWorkInboxFence.Id(row.ExceptionId),
            row.ProposedAtUtc.ToUniversalTime(), ScreenPath, row.ProposedBy, rules,
            [LogisticsWorkInboxFence.InstructionReference(row.InstructionId)]);
        return new(context.CompanyId, item, version.ToString(System.Globalization.CultureInfo.InvariantCulture), status == 1,
            LogisticsPlanExcess: new(instructionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            LogisticsPlanExcessProof: new(context.CompanyId, row.ExceptionId, row.InstructionId, row.LineId,
                LogisticsStore.PlanExcessQuantity(row.Quantity), row.Reason, row.ProposedBy, row.ProposedAtUtc));
    }

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } refused)
        {
            return refused;
        }

        var companyId = context.CompanyId;
        const int issued = (int)LoadingInstructionStatus.Issued;
        List<Pending> pending;
        try
        {
            var read = await rows.ListAsync(
                (from exception in rows.PlanExcessExceptions
                 join instruction in rows.Instructions
                     on new { exception.CompanyId, Id = exception.InstructionId } equals new { instruction.CompanyId, instruction.Id }
                 join line in rows.Lines
                     on new { exception.CompanyId, exception.InstructionId, exception.LineId }
                     equals new { line.CompanyId, line.InstructionId, line.LineId }
                 where exception.CompanyId == companyId && instruction.CompanyId == companyId && line.CompanyId == companyId
                     && exception.Status == PendingStatus && exception.Version == PendingVersion && instruction.Status == issued
                 orderby exception.ProposedAtUtc, exception.ExceptionId
                 select new
                 {
                     exception.ExceptionId,
                     exception.InstructionId,
                     exception.Quantity,
                     exception.ProposedBy,
                     exception.ProposedAtUtc,
                     line.Instructed,
                     line.ApprovedPlanExcess,
                     line.UnitIsKilogram,
                     line.IsBlocked,
                 })
                .Take(maxCount + 1),
                cancellationToken);
            pending = [.. read.Select(row => new Pending(row.ExceptionId, row.InstructionId, row.Quantity, row.ProposedBy,
                row.ProposedAtUtc, row.Instructed, row.ApprovedPlanExcess, row.UnitIsKilogram, row.IsBlocked))];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.UnavailableAsync(context, ApproverPermission, cancellationToken);
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(Math.Min(pending.Count, maxCount));
        foreach (var row in pending.Take(maxCount))
        {
            if (row.ExceptionId == Guid.Empty || row.InstructionId == Guid.Empty || string.IsNullOrWhiteSpace(row.ProposedBy)
                || row.Quantity <= 0m || row.Instructed < 0m || row.ApprovedPlanExcess < 0m)
            {
                // A record Logistics cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            var unit = row.UnitIsKilogram ? KilogramUnit : null;
            var rules = new List<WorkApprovalRuleResultV1>(3)
            {
                new(ExcessRule, WorkInboxRulesV1.Decimal(row.Instructed), null, WorkInboxRulesV1.Decimal(row.Quantity), unit),
            };
            if (row.ApprovedPlanExcess > 0m)
            {
                rules.Add(new(AlreadyApprovedRule, null, WorkInboxRulesV1.Decimal(row.ApprovedPlanExcess), null, unit));
            }

            if (row.IsBlocked)
            {
                rules.Add(new(LineBlockedRule, null, null, null, null));
            }

            items.Add(new WorkInboxItemV1(ModuleName, KindName, row.ExceptionId, LogisticsWorkInboxFence.Id(row.ExceptionId),
                row.ProposedAtUtc.ToUniversalTime(), ScreenPath, row.ProposedBy, rules,
                [LogisticsWorkInboxFence.InstructionReference(row.InstructionId)]));
        }

        return WorkInboxSourceResultV1.Found(companyId, items, pending.Count > maxCount);
    }

    private sealed record Pending(Guid ExceptionId, Guid InstructionId, decimal Quantity, string ProposedBy,
        DateTimeOffset ProposedAtUtc, decimal Instructed, decimal ApprovedPlanExcess, bool UnitIsKilogram, bool IsBlocked);
}
