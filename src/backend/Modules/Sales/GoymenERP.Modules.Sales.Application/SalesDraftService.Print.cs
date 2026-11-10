using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// MOD-07: the price facts of the sales order PDF (see <see cref="ISalesOrderPrintReadServiceV1"/>) — with the price read, the
/// printed revision's line and document totals with VAT and withholding from the one <see cref="SalesOrderTotalsCalculator"/>
/// (API-03 / SAT-D11 / SAT-D75), wired exactly as the totals read wires them.
/// </summary>
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
                [.. draft.Lines.OrderBy(line => line.LineNo).Select(line => Pricing(line.Id, line.LineNo, line.Commercial))],
                null, CurrentTotals(draft))
            : new SalesOrderPrintPricingV1(version, SalesPriceDisclosure.Visible, currencyCode,
                [.. accepted.Lines.OrderBy(line => line.LineNo).Select(line => Pricing(line.LineId, line.LineNo, line.Commercial))],
                printed, AcceptedTotals(draft, accepted));
    }

    /// <summary>MOD-07 → API-03: the current revision's totals, each line with its OWN tax snapshot (as the totals read).</summary>
    private static SalesOrderPrintTotalsV1 CurrentTotals(SalesOrderDraft draft) =>
        PrintTotals(SalesDraftApiV1.RevisionText(draft.Revision), SalesOrderTotalsCalculator.CalculateWithLineTax(draft),
            draft.Lines.ToDictionary(line => line.Id, line => new PrintedLine(line.OrderUnit.UnitId, line.QuantityText, line.Tax)));

    /// <summary>
    /// SAT-D165 + MOD-07 → API-03: the ACCEPTED revision's totals — its own quantities, commercial fields and tax snapshots
    /// (SAT-D115/D166), in the currency and money scale it was approved with (the risk read's archived basis rule). Null when the
    /// revision's amounts cannot be read (a quantity that is not an exact decimal, a line the calculator refuses): the PDF is then
    /// not printed (code-less 503), never printed with the current revision's or guessed totals.
    /// </summary>
    private static SalesOrderPrintTotalsV1? AcceptedTotals(SalesOrderDraft draft, ApprovedRevisionSnapshot accepted)
    {
        var inputs = new List<SalesTotalsLineInput>(accepted.Lines.Count);
        foreach (var line in accepted.Lines.OrderBy(line => line.LineNo))
        {
            if (!SalesLinePriceCalculator.TryParseExact(line.QuantityText, out var quantity) || quantity < 0m)
            {
                return null;
            }

            inputs.Add(new SalesTotalsLineInput(line.LineId, line.LineNo, line.OrderUnitId, quantity, line.Commercial, line.Tax));
        }

        SalesOrderTotals totals;
        try
        {
            totals = SalesOrderTotalsCalculator.CalculateWithLineTax(inputs, accepted.Header?.CurrencyId ?? draft.Header.CurrencyId,
                Math.Min(accepted.MoneyScale, Money.MaximumScale));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or SalesRuleException)
        {
            return null;
        }

        return PrintTotals(SalesDraftApiV1.RevisionText(accepted.Revision), totals,
            accepted.Lines.ToDictionary(line => line.LineId, line => new PrintedLine(line.OrderUnitId, line.QuantityText, line.Tax)));
    }

    private sealed record PrintedLine(Guid OrderUnitId, string Quantity, SalesLineTaxSnapshot? Tax);

    private static SalesOrderPrintTotalsV1 PrintTotals(string revision, SalesOrderTotals totals,
        IReadOnlyDictionary<Guid, PrintedLine> lines) =>
        new(revision, WireDocument(totals.Document),
            [.. totals.Lines.OrderBy(line => line.LineNo).Select(line => PrintLine(line, lines[line.LineId]))]);

    /// <summary>
    /// One printed line: the wired totals and — only when the line's tax was computed — the snapshot's withholding percent (when
    /// withholding applies) and its exemption / withholding codes resolved by Settings, verbatim. A NoSource line prints no code.
    /// </summary>
    private static SalesOrderPrintLineTotalV1 PrintLine(SalesLineTotal line, PrintedLine printed)
    {
        var tax = line.TaxState == SalesTaxState.Computed ? printed.Tax : null;
        return new SalesOrderPrintLineTotalV1(
            WireLine(line, printed.OrderUnitId, printed.Quantity),
            line.TaxRate?.WithholdingShare is not null && tax?.WithholdingPercent is { } percent
                ? SalesLinePriceCalculator.Format(percent)
                : null,
            tax?.ExemptionCodeState == SalesLineTaxStates.Resolved ? tax.ExemptionCode : null,
            tax?.WithholdingCodeState == SalesLineTaxStates.Resolved ? tax.WithholdingCode : null);
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
