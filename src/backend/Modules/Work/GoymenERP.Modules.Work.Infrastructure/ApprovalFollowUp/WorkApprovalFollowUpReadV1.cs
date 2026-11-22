using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.ApprovalFollowUp;

/// <summary>
/// WP2 (YOL-D17, YOL-D28): the "yükseltilen" and "sahipsiz" views over Work's own follow-up rows. The caller needs
/// <c>work.approvals.escalate</c> company-wide now (checked before and after the read); an escalated row whose rule named
/// another escalation permission is shown only to a holder of that one too. A cycle leaves both views as soon as its
/// owner decision (D10) or its observed resolution is recorded; an orphaned cycle also leaves when an eligible approver
/// was told later (a waiting step after the orphaned one). Reads only; decides nothing.
/// </summary>
public sealed class WorkApprovalFollowUpReadV1(
    WorkDbContext database,
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority) : IWorkApprovalFollowUpReadV1
{
    public const int PageSize = 100;

    public async Task<WorkApprovalFollowUpPageV1> ListAsync(string? view, CancellationToken cancellationToken = default)
    {
        if (view is not (WorkApprovalFollowUpViewsV1.Escalated or WorkApprovalFollowUpViewsV1.Orphaned))
        {
            throw new WorkApprovalFollowUpException(400, "validation_failed");
        }

        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
        {
            throw new WorkApprovalFollowUpException(401, "unauthenticated");
        }

        var actor = user.UserId;
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
        {
            throw new WorkApprovalFollowUpException(403, "forbidden");
        }

        var companyId = company.CompanyId;
        await RequireCompanyWideAsync(companyId, actor, WorkPermissionManifestV1.ApprovalsEscalate, cancellationToken);
        var step = view == WorkApprovalFollowUpViewsV1.Escalated
            ? (int)WorkApprovalFollowUpStepV1.Escalated
            : (int)WorkApprovalFollowUpStepV1.Orphaned;
        var resolved = (int)WorkApprovalInboxEventTypeV1.ResolvedObserved;
        var decided = (int)WorkApprovalInboxEventTypeV1.Decided;
        var waiting = (int)WorkApprovalFollowUpStepV1.Waiting;
        var query = database.ApprovalFollowUps.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Step == step
                && !database.ApprovalInboxEvents.Any(fact => fact.CompanyId == row.CompanyId && fact.Kind == row.Kind
                    && fact.ItemId == row.ItemId && fact.CycleStartedAtUtc == row.CycleStartedAtUtc
                    && (fact.EventType == resolved || fact.EventType == decided)));
        if (step == (int)WorkApprovalFollowUpStepV1.Orphaned)
        {
            query = query.Where(row => !database.ApprovalFollowUps.Any(other => other.CompanyId == row.CompanyId
                && other.Kind == row.Kind && other.ItemId == row.ItemId && other.CycleStartedAtUtc == row.CycleStartedAtUtc
                && other.Step == waiting && other.RecordedAtUtc >= row.RecordedAtUtc));
        }

        var rows = await query.OrderBy(row => row.CycleStartedAtUtc).ThenBy(row => row.Kind).ThenBy(row => row.ItemId)
            .Take(PageSize + 1).ToArrayAsync(cancellationToken);
        RequireStill(companyId, actor);

        var allowed = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [WorkPermissionManifestV1.ApprovalsEscalate] = true,
        };
        var items = new List<WorkApprovalFollowUpItemV1>();
        foreach (var row in rows.Take(PageSize))
        {
            if (step == (int)WorkApprovalFollowUpStepV1.Escalated && row.EscalationPermission is { } named)
            {
                if (!allowed.TryGetValue(named, out var holds))
                {
                    holds = await HoldsCompanyWideAsync(companyId, actor, named, cancellationToken);
                    allowed[named] = holds;
                }

                if (!holds)
                {
                    continue;
                }
            }

            items.Add(new WorkApprovalFollowUpItemV1(row.Kind, row.ItemId, row.DocumentLabel, row.Link, row.CycleStartedAtUtc,
                WorkApprovalFollowUpStepsV1.Wire(row.Step), row.ApproverPermission, row.EscalationPermission, row.RuleVersion,
                row.DueAtUtc, row.RecordedAtUtc, row.RecipientCount));
        }

        await RequireCompanyWideAsync(companyId, actor, WorkPermissionManifestV1.ApprovalsEscalate, cancellationToken);
        return new WorkApprovalFollowUpPageV1(companyId, view, items, rows.Length > PageSize);
    }

    private async Task RequireCompanyWideAsync(Guid companyId, string actor, string permission, CancellationToken cancellationToken)
    {
        if (!await HoldsCompanyWideAsync(companyId, actor, permission, cancellationToken))
        {
            throw new WorkApprovalFollowUpException(403, "forbidden");
        }
    }

    private async Task<bool> HoldsCompanyWideAsync(Guid companyId, string actor, string permission, CancellationToken cancellationToken)
    {
        PermissionScope scope;
        try
        {
            scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new WorkApprovalFollowUpException(503, "service_unavailable");
        }

        RequireStill(companyId, actor);
        return scope is { IsGranted: true, IsCompanyWide: true };
    }

    private void RequireStill(Guid companyId, string actor)
    {
        if (!user.IsAuthenticated || !string.Equals(user.UserId, actor, StringComparison.Ordinal) || !company.IsResolved
            || company.CompanyId != companyId || !user.CanAccessCompany(companyId))
        {
            throw new WorkApprovalFollowUpException(403, "forbidden");
        }
    }
}
