using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.WorkInbox;

/// <summary>
/// WP7 (AI-D31, AI-D32, YOL-D18; KIM-D1, ADR-004 §6): the role access grant requests waiting for the second person's decision,
/// as Identity lists them for the Work inbox. A request waits while it is Pending — exactly what
/// <c>ApproveIdentityAccessGrantRequestV1</c> accepts; it is decided on the "Kullanıcılar ve yetkiler" screen (the target user
/// selected) by a holder of <c>identity.access.approve</c> granted company-wide (the inbox decides nothing, and it never grants).
/// Every pending request of the company is returned: Work hides it from the owner's "may not decide" set (D4) —
/// <see cref="AccessGrantRequest.IsRequesterOrTarget"/> for a request that needs a second approval (the requester as maker and the
/// target user); the owner refuses no one on a request that needs none, so that item names no maker.
/// <para>
/// The fence is the owner's own: the server-resolved company and the authenticated user must be the context's, the user must
/// still reach the company and <c>identity.access.approve</c> must be granted company-wide as it stands now (before and after the
/// read). The label is the requested role's name as Identity shows it; the target user and the role are the records the request
/// rests on (display names, as the pending list shows them). A role or user the read did not return is unavailable, never skipped.
/// </para>
/// </summary>
internal sealed class IdentityAccessGrantRequestWorkInboxSourceV1(
    IdentityDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority) : IWorkInboxSourceV1
{
    public const string ModuleName = "identity";
    public const string KindName = "identity.access-grant-request.v1";

    /// <summary>The request waits for a decision of a holder of the access approval permission.</summary>
    public const string ApprovalRequiredRule = "identity.access-grant-approval-required";

    /// <summary>The requested role is critical: it needs a second person's approval (KIM-D1).</summary>
    public const string SecondApprovalRule = "identity.access-grant-second-approval";

    /// <summary>The request asks for an assignment that ends on a date (a time-boxed grant).</summary>
    public const string TimeBoxedRule = "identity.access-grant-time-boxed";

    /// <summary>The "Kullanıcılar ve yetkiler" screen (<c>IDENTITY_ROUTE accessList</c>) and its selected-user parameter.</summary>
    internal const string AccessScreenPath = "/ayarlar/kullanicilar-ve-yetkiler";

    internal const string SelectedUserParameter = "kullanici";

    /// <summary>The role screen (<c>IDENTITY_ROUTE roleDetail</c>, <c>/ayarlar/kullanicilar-ve-yetkiler/roller/:roleId</c>).</summary>
    internal const string RoleScreenPath = "/ayarlar/kullanicilar-ve-yetkiler/roller";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => IdentityPermissionManifestV1.AccessApprove;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await CheckAsync(context, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<Pending> rows;
        int total;
        try
        {
            var companyId = context.CompanyId;
            var pending = database.AccessGrantRequests.AsNoTracking()
                .Where(request => request.CompanyId == companyId && request.Status == AccessGrantRequestStatus.Pending);
            total = await pending.CountAsync(cancellationToken);
            rows = await (
                    from request in pending
                    join role in database.Roles.AsNoTracking() on request.RoleId equals role.Id into roles
                    from role in roles.DefaultIfEmpty()
                    join target in database.Users.AsNoTracking() on request.TargetUserId equals target.Id into targets
                    from target in targets.DefaultIfEmpty()
                    orderby request.RequestedAtUtc, request.Id
                    select new Pending(
                        request.Id,
                        request.RequestedByUserId,
                        request.TargetUserId,
                        request.RoleId,
                        request.RequiresSecondApproval,
                        request.RequestedAtUtc,
                        request.ValidUntilUtc,
                        role == null ? null : role.Name,
                        target == null ? null : target.DisplayName))
                .Take(maxCount)
                .ToListAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await CheckAsync(context, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await CheckAsync(context, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(rows.Count);
        foreach (var row in rows)
        {
            if (!TryItem(row, out var item))
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, total > rows.Count);
    }

    /// <summary>Null when the caller may read; otherwise the outcome to return (an unreadable authority is unavailable).</summary>
    private async Task<WorkInboxSourceResultV1?> CheckAsync(WorkInboxSourceContextV1 context, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return WorkInboxSourceResultV1.Denied();
        }

        try
        {
            var scope = await authority.RevalidatePermissionScopeAsync(ApproverPermission, context.CompanyId, cancellationToken);
            return scope is { IsGranted: true, IsCompanyWide: true } && SameContext(context)
                ? null
                : WorkInboxSourceResultV1.Denied();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WorkInboxSourceResultV1.Unavailable();
        }
    }

    private bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    private static bool TryItem(Pending row, out WorkInboxItemV1 item)
    {
        item = null!;
        if (row.Id == Guid.Empty || row.RequestedBy == Guid.Empty || row.TargetUserId == Guid.Empty || row.RoleId == Guid.Empty
            || string.IsNullOrWhiteSpace(row.RoleName) || row.RoleName.Length > WorkInboxRulesV1.MaxDocumentLabelLength
            || string.IsNullOrWhiteSpace(row.TargetName) || row.TargetName.Length > WorkInboxRulesV1.MaxReferenceLabelLength)
        {
            return false;
        }

        var rules = new List<WorkApprovalRuleResultV1>(3) { new(ApprovalRequiredRule, null, null, null, null) };
        if (row.RequiresSecondApproval)
        {
            rules.Add(new(SecondApprovalRule, null, null, null, null));
        }

        if (row.ValidUntilUtc is not null)
        {
            rules.Add(new(TimeBoxedRule, null, null, null, null));
        }

        var link = AccessScreenPath + "?" + SelectedUserParameter + "=" + Text(row.TargetUserId);
        var references = new List<WorkApprovalReferenceV1>(2)
        {
            new("target-user", row.TargetName, row.TargetUserId, link),
            new("role", row.RoleName, row.RoleId, RoleScreenPath + "/" + Text(row.RoleId)),
        };
        // The owner refuses the requester and the target only on a request that needs a second approval (Approve).
        string? preparedBy = row.RequiresSecondApproval ? Text(row.RequestedBy) : null;
        IReadOnlyList<string>? excluded = row.RequiresSecondApproval ? [Text(row.TargetUserId)] : null;
        item = new WorkInboxItemV1(ModuleName, KindName, row.Id, row.RoleName, row.RequestedAtUtc.ToUniversalTime(), link,
            preparedBy, rules, references, excluded);
        return true;
    }

    private static string Text(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private sealed record Pending(
        Guid Id,
        Guid RequestedBy,
        Guid TargetUserId,
        Guid RoleId,
        bool RequiresSecondApproval,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? ValidUntilUtc,
        string? RoleName,
        string? TargetName);
}
