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
        RequirePrintable(draft);
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
    /// A newer revision in preparation or awaiting approval is printed as a draft (SAT-D56), as before.
    /// </summary>
    private static ApprovedRevisionSnapshot? PrintedAcceptedRevision(SalesOrderDraft draft) =>
        draft.ApprovalState == InternalApprovalState.Approved && draft.CustomerAcceptedFallback is { } accepted ? accepted : null;

    /// <summary>
    /// K3-REVIEW-B round 2 P2-1 (fail-closed): the accepted revision names another customer or currency than the header — it
    /// cannot be printed under its own header, and the rejected revision is never printed in its place (the "print the rejected
    /// one under a watermark" option was not chosen, EK23). 409 <c>sales_order_pdf_customer_revision_pending</c>, stable until
    /// the customer/currency revision is resolved. Asked before any price read; nothing is recorded (a read).
    /// </summary>
    private static void RequirePrintable(SalesOrderDraft draft)
    {
        if (PrintedAcceptedRevision(draft) is { } accepted && SalesOrderDraft.HeaderDiffersFrom(draft.Header, accepted))
        {
            throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.OrderPdfCustomerRevisionPending);
        }
    }

    private static SalesOrderPrintRevisionV1 PrintRevision(ApprovedRevisionSnapshot accepted) =>
        new(SalesDraftApiV1.RevisionText(accepted.Revision),
            [.. accepted.Lines.OrderBy(line => line.LineNo)
                .Select(line => new SalesOrderPrintRevisionLineV1(line.LineId, line.LineNo, line.ProductId, line.OrderUnitId, line.QuantityText))],
            new SalesDraftApprovalV1(accepted.Approval.SubmittedBy, accepted.Approval.SubmittedAtUtc, accepted.Approval.DecidedBy,
                accepted.Approval.DecidedAtUtc, null));

    private static SalesOrderPrintPricingV1 Withheld(string version, SalesOrderPrintRevisionV1? printed) =>
        new(version, SalesPriceDisclosure.Withheld, null, [], printed);
}
