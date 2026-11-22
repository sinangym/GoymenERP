using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31): the fence every Purchasing Work inbox source applies before and after its read. The server-resolved company
/// and the authenticated user must be the context's, the user must still reach the company, and the source's approver
/// permission must be granted company-wide as it stands now. Anything else is a refusal without items; an authority answer that
/// cannot be read is unavailable, never a grant.
/// </summary>
internal sealed class PurchasingWorkInboxFence(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
{
    /// <summary>The trailing identity characters the Purchasing order list shows for an order without a document number.</summary>
    internal const int ShortReferenceLength = 8;

    /// <summary>The order screen (<c>PURCHASING_ROUTE orderDetail</c>, <c>/satinalma/siparisler/:orderId</c>).</summary>
    internal const string OrderScreenPath = "/satinalma/siparisler";

    /// <summary>The purchase request screen (<c>PURCHASING_ROUTE requestDetail</c>, <c>/satinalma/talepler/:requestId</c>).</summary>
    internal const string RequestScreenPath = "/satinalma/talepler";

    /// <summary>The incoming invoice screen (<c>PURCHASING_ROUTE invoiceDetail</c>, <c>/satinalma/faturalar/:invoiceId</c>).</summary>
    internal const string InvoiceScreenPath = "/satinalma/faturalar";

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

    public bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    /// <summary>The order label as the Purchasing order list shows it: the document number, else "…" and the id's tail.</summary>
    public static string OrderLabel(Guid orderId, string? documentNumber) =>
        string.IsNullOrWhiteSpace(documentNumber) ? ShortReference(orderId) : documentNumber;

    /// <summary>The short reference the Purchasing screens show for an identity without a label: "…" and its last characters.</summary>
    public static string ShortReference(Guid id) =>
        "…" + id.ToString("D", CultureInfo.InvariantCulture)[^ShortReferenceLength..];

    public static string Link(string basePath, Guid id) =>
        basePath + "/" + id.ToString("D", CultureInfo.InvariantCulture);
}
