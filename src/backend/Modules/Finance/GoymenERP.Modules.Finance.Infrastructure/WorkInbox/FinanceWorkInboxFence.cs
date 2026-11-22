using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31): the fence every Finance Work inbox source applies before and after its read. The server-resolved company
/// and the authenticated user must be the context's, the user must still reach the company, and the source's approver
/// permission must be granted company-wide as it stands now. Anything else is a refusal without items.
/// </summary>
internal sealed class FinanceWorkInboxFence(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
{
    /// <summary>The short reference the Finance screens show for an identity (first eight characters, lowercase).</summary>
    internal const int ShortReferenceLength = 8;

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

    public bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    public static string ShortReference(Guid id) =>
        id.ToString("D", CultureInfo.InvariantCulture)[..ShortReferenceLength];

    public static string Link(string basePath, Guid id) =>
        basePath + "/" + id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Exact stored decimal text (Finance stores amounts as invariant text, never rounded).</summary>
    public static bool TryAmount(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
}
