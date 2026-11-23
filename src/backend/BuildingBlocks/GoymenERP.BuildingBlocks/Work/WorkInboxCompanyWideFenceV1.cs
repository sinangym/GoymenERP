using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.BuildingBlocks.Work;

/// <summary>
/// WP7: the fence a Work inbox source applies before and after its read when its owner's decision command measures one or more
/// permissions company-wide. The server-resolved company and the authenticated user must be the context's, the user must still
/// reach the company, and every given permission must be granted company-wide as it stands now (fresh revalidation). Anything
/// else is a refusal without items; an authority answer that cannot be read is unavailable, never a grant.
/// </summary>
public sealed class WorkInboxCompanyWideFenceV1(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
{
    /// <summary>Null when the caller may read; otherwise the outcome to return.</summary>
    public async Task<WorkInboxSourceResultV1?> CheckAsync(
        WorkInboxSourceContextV1 context,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(permissions);
        if (!SameContext(context) || permissions.Count == 0)
        {
            return WorkInboxSourceResultV1.Denied();
        }

        try
        {
            foreach (var permission in permissions)
            {
                var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
                if (scope is not { IsGranted: true, IsCompanyWide: true })
                {
                    return WorkInboxSourceResultV1.Denied();
                }
            }

            return SameContext(context) ? null : WorkInboxSourceResultV1.Denied();
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

    public bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    /// <summary>The short reference the screens show for an identity without a label: "…" and its last eight characters.</summary>
    public static string ShortReference(Guid id) =>
        "…" + id.ToString("D", System.Globalization.CultureInfo.InvariantCulture)[^8..];

    public static string Link(string basePath, Guid id) =>
        basePath + "/" + id.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
}
