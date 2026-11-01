using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>MOD-07: the price facts of the sales order PDF (see <see cref="ISalesOrderPrintReadServiceV1"/>).</summary>
public sealed partial class SalesDraftService : ISalesOrderPrintReadServiceV1
{
    public async Task<SalesOrderPrintPricingV1> GetOrderPrintPricingAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var draft = await LoadDraftAsync(scope, permission, draftId, cancellationToken);
        var version = draft.Version.ToString(CultureInfo.InvariantCulture);
        var accepted = PrintedAcceptedRevision(draft);
        var printed = accepted is null ? null : PrintRevision(accepted);
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            return Withheld(version, printed);
        }

        // The currency code is a price fact; the Settings read is fenced under the draft read like every owner read,
        // and the price read is asked again AFTER it, so a revocation in between releases nothing.
        var currencyCode = await CurrencyCodeAsync(scope, permission, draft, cancellationToken);
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            return Withheld(version, printed);
        }

        return accepted is null
            ? new SalesOrderPrintPricingV1(version, SalesPriceDisclosure.Visible, currencyCode,
                [.. draft.Lines.OrderBy(line => line.LineNo).Select(line => Pricing(line.Id, line.LineNo, line.Commercial))])
            : new SalesOrderPrintPricingV1(version, SalesPriceDisclosure.Visible, currencyCode,
                [.. accepted.Lines.OrderBy(line => line.LineNo).Select(line => Pricing(line.LineId, line.LineNo, line.Commercial))],
                printed);
    }

    /// <summary>
    /// SAT-D165 (EK23): the revision the customer last accepted, printed instead of the current one, when the CURRENT
    /// revision is internally approved and the customer rejected it (<see cref="SalesOrderDraft.CustomerAcceptedFallback"/>).
    /// Not when that revision names another customer or currency than the header (the header printed would not be the accepted
    /// one; the order ships nothing then): the current revision is printed under the draft watermark with the rejection note.
    /// A newer revision in preparation or awaiting approval is printed as a draft (SAT-D56), as before.
    /// </summary>
    private static ApprovedRevisionSnapshot? PrintedAcceptedRevision(SalesOrderDraft draft) =>
        draft.ApprovalState == InternalApprovalState.Approved
        && draft.CustomerAcceptedFallback is { } accepted
        && !SalesOrderDraft.HeaderDiffersFrom(draft.Header, accepted)
            ? accepted
            : null;

    private static SalesOrderPrintRevisionV1 PrintRevision(ApprovedRevisionSnapshot accepted) =>
        new(SalesDraftApiV1.RevisionText(accepted.Revision),
            [.. accepted.Lines.OrderBy(line => line.LineNo)
                .Select(line => new SalesOrderPrintRevisionLineV1(line.LineId, line.LineNo, line.ProductId, line.OrderUnitId, line.QuantityText))]);

    private static SalesOrderPrintPricingV1 Withheld(string version, SalesOrderPrintRevisionV1? printed) =>
        new(version, SalesPriceDisclosure.Withheld, null, [], printed);
}
