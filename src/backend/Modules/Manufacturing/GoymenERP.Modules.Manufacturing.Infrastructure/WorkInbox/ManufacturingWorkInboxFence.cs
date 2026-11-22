using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31): the fence every Manufacturing Work inbox source applies before and after its read. The server-resolved company
/// and the authenticated user must be the context's, the user must still reach the company, and the source's approver
/// permission must be granted company-wide as it stands now. Anything else is a refusal without items; an authority read
/// that fails is unavailable, never a grant.
/// </summary>
internal sealed class ManufacturingWorkInboxFence(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
{
    /// <summary>The plan detail screen (<c>/uretim/plan/:planId</c>), where the approval and the re-approval are given.</summary>
    public const string PlanDetailPath = "/uretim/plan";

    /// <summary>The order detail screen (<c>/uretim/emir/:orderId</c>).</summary>
    public const string OrderDetailPath = "/uretim/emir";

    /// <summary>Null when the caller may read; otherwise the outcome to return.</summary>
    public async Task<WorkInboxSourceResultV1?> CheckAsync(
        WorkInboxSourceContextV1 context,
        string permission,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return WorkInboxSourceResultV1.Denied();
        }

        try
        {
            var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
            return scope.IsGranted && scope.IsCompanyWide && SameContext(context) ? null : WorkInboxSourceResultV1.Denied();
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

    /// <summary>A failed read answers the fence's refusal when the caller lost access meanwhile, else unavailable.</summary>
    public async Task<WorkInboxSourceResultV1> UnavailableAsync(
        WorkInboxSourceContextV1 context,
        string permission,
        CancellationToken cancellationToken) =>
        await CheckAsync(context, permission, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();

    public bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    /// <summary>The identity as the Manufacturing screens carry it (lowercase, hyphenated).</summary>
    public static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    public static string PlanLink(Guid planId) => PlanDetailPath + "/" + Id(planId);

    public static string OrderLink(Guid orderId) => OrderDetailPath + "/" + Id(orderId);
}
