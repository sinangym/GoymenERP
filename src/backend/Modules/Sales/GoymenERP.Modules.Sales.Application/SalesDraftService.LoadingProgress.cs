using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// DEC-SAL-005: the read-only per-order loading facts needed by the Sales detail. Logistics remains the owner; Sales
/// reads through <see cref="ISalesOrderLoadingReadPortV1"/> and stores no loading copy. The existing draft-read name is
/// measured before the Sales read and around the owner read; the owner port carries no authority of its own.
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftLoadingProgressResponse> GetDraftLoadingProgressAsync(Guid draftId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesOrderDraft? draft;
        try
        {
            draft = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (draft is null)
        {
            // Another company's draft is null under the Sales tenant filter; Logistics is never asked for it.
            throw new SalesDraftServiceException(404, "not_found");
        }

        Dictionary<Guid, LineLoading>? loading;
        try
        {
            loading = await ReadLoadingAsync(scope, bindings.ReadPermission, draft, cancellationToken);
        }
        catch (OverflowException)
        {
            // A malformed owner answer must not escape as a server error or be shown as a plausible quantity.
            loading = null;
        }

        if (loading is null)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.LoadingProviderUnavailable);
        }

        return new SalesOrderDraftLoadingProgressResponse(
            draft.Version.ToString(CultureInfo.InvariantCulture),
            [.. draft.Lines.OrderBy(line => line.LineNo).Select(line => LoadingProgress(line, loading.GetValueOrDefault(line.Id)))]);
    }

    private static SalesDraftLineLoadingProgressV1 LoadingProgress(SalesOrderDraftLine line, LineLoading? loading)
    {
        loading ??= new LineLoading(0, 0m, 0m, 0m, 0m);
        return new SalesDraftLineLoadingProgressV1(
            line.Id,
            line.LineNo,
            line.OrderUnit.UnitId,
            line.QuantityText,
            loading.Count,
            Text(loading.Instructed, line.Quantity.Unit),
            Text(loading.Loaded, line.Quantity.Unit),
            Text(loading.Shipped, line.Quantity.Unit),
            Text(loading.Delivered, line.Quantity.Unit));
    }
}
