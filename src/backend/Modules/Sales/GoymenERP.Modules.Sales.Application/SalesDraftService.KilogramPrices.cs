using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D436 / SAT-D439 (AK-5): the order's kg price read. Figures come from <see cref="SalesKilogramPriceCalculator"/> only; the
/// screen never recomputes. Information only: no amount, cost or stock value changes (SAT-D434/D435).
/// </summary>
public sealed partial class SalesDraftService
{
    /// <summary>
    /// SAT-D436 / SAT-D439 / SAT-D265(5): the kg price indicator is OPEN. Its independent golden (ADET_KG_FORMUL_GOLDEN_V1 §1, rows
    /// K01-K19, TK1-TK9) travels in this train (PR #1201) with the decision file it cites; the rows are pinned by row id in
    /// SalesKilogramPriceGoldenTests and SalesDraftServiceTests.KilogramPrices. The train's exact-head K3 acceptance still decides
    /// the merge. Closed (the named parameter of the overloads below), the read answers FormulaGoldenPending only. N2 stays
    /// open: a document figure is hidden while any line is not a kg-bearing goods line (K17/K18).
    /// </summary>
    internal static bool KilogramPriceGoldenAccepted => true;

    /// <summary>
    /// SAT-D452 / SAT-D457 (SAT-D436 TK6): the ONE named rule for which invoices of an order enter its realization ("faturalanan
    /// tutar", actual kg, actual kg price). Only a finalised invoice counts (SAT-D452): a draft, a pending or an approved invoice
    /// is not yet an invoiced amount, and a rejected, cancelled or officially cancelled one never is. A finalised invoice Finance
    /// refused does not count (SAT-D457, golden EK1 E21); it enters again only once Finance admits a re-drive (E21a). A re-drive
    /// answered otherwise (unavailable, key conflict, retry, denied) is no admission: the refusal still stands and the invoice stays
    /// out. The hand-off ledger keeps the last answer only, so once more than one answer is recorded without an admission an
    /// earlier refusal cannot be ruled out and the invoice stays out (fail-closed; it enters when Finance admits it). A finalised
    /// invoice Finance has not answered yet, or answered once without a refusal, counts: the decision excludes the refusal only.
    /// </summary>
    internal static bool EntersKilogramPriceRealization(SalesInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.State != SalesInvoiceState.Finalized) return false;
        return invoice.Finalization switch
        {
            null => true,
            { FinanceStatus: SalesInvoiceFinanceStatus.Admitted } => true,
            { AttemptCount: 0 } => true,
            { AttemptCount: 1 } waiting => !SalesInvoiceService.IsRefusedFinanceOutcome(waiting.LastOutcome),
            _ => false,
        };
    }

    public Task<SalesOrderKilogramPricesResponse> GetDraftKilogramPricesAsync(Guid draftId, CancellationToken cancellationToken = default) =>
        GetDraftKilogramPricesAsync(draftId, KilogramPriceGoldenAccepted, cancellationToken);

    /// <summary>The same read with the AK-5 switch named, so the closed and the open path are both measured by tests.</summary>
    internal async Task<SalesOrderKilogramPricesResponse> GetDraftKilogramPricesAsync(Guid draftId, bool kilogramPriceOpen,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty) throw new SalesDraftServiceException(404, "not_found");
        var draft = await LoadDraftAsync(scope, bindings.ReadPermission, draftId, cancellationToken);
        // A kg price is a price: without the price read nothing of it is disclosed.
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
            throw new SalesDraftServiceException(403, "forbidden");
        if (!kilogramPriceOpen)
            return new SalesOrderKilogramPricesResponse(SalesKilogramPriceStatesV1.FormulaGoldenPending, draft.Header.CurrencyId);

        IReadOnlyList<SalesInvoice> invoices = [];
        if (invoiceRecords is not null)
        {
            SalesOrderInvoicePage page;
            try
            {
                page = await GuardedAsync(scope, bindings.ReadPermission,
                    () => invoiceRecords.ListInvoicesOfOrderAsync(draft.Id, SalesInvoiceApiV1.MaximumDraftInvoices, cancellationToken),
                    cancellationToken);
            }
            catch (SalesDraftStoreException exception)
            {
                throw Store(exception);
            }

            // A partial invoice history would understate the realization: fail closed rather than show a wrong figure.
            if (page.HasMore) throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
            invoices = page.Items;
        }
        else throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);

        await RequireStillAsync(scope, bindings.ReadPermission, cancellationToken);
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
            throw new SalesDraftServiceException(403, "forbidden");
        return KilogramPrices(draft, invoices);
    }

    /// <summary>The computed read (internal for the golden-pinned tests of the open path).</summary>
    internal static SalesOrderKilogramPricesResponse KilogramPrices(SalesOrderDraft draft, IReadOnlyList<SalesInvoice> invoices)
    {
        var totals = SalesOrderTotalsCalculator.CalculateWithLineTax(draft);
        var activeLines = invoices.Where(EntersKilogramPriceRealization).SelectMany(invoice => invoice.Lines).ToList();
        var lines = new List<SalesOrderLineKilogramPricesV1>();
        var theoreticalDocument = new List<SalesKilogramPriceLine>();
        var actualDocument = new List<SalesKilogramPriceLine>();
        foreach (var line in draft.Lines.OrderBy(item => item.LineNo))
        {
            var total = totals.Lines.Single(item => item.LineId == line.Id);
            // TK1: the KDV-excluded line net after the line discount. A maturity surcharge is not a goods amount (SAT-D465 keeps it
            // out of the kg price) and the invoice amounts carry no pre-surcharge net, so until the SAT-D465 slice and its golden
            // (EK2) land, a surcharged line fails closed on every figure: line and document, theoretical and actual (K18 / N2).
            var surcharged = total.NetAmountBeforeSurcharge is { } before && total.NetAmount is { } net && before.Amount != net.Amount;
            var amount = surcharged ? null : total.NetAmount?.Amount;
            var perUnit = line.OrderUnit.IsKilogram ? (decimal?)1m : draft.KilogramsPerUnitOf(line.Id)?.KilogramsPerUnit;
            var theoreticalKg = TheoreticalKg(line.Quantity.Value, perUnit);
            theoreticalDocument.Add(new(amount, theoreticalKg));

            var invoiced = activeLines.Where(item => item.Share.SalesOrderLineId == line.Id).ToList();
            var invoicedQuantity = invoiced.Sum(item => item.Share.Quantity);
            decimal? invoicedAmount = invoiced.Count == 0 ? null : invoiced.Sum(item => item.Amounts.Net.Amount);
            // TK6: the actual kg is the scale kg written on each invoiced share (a kg line's invoiced quantity is that kg).
            decimal? actualKg = invoiced.Count == 0 || invoiced.Any(item => !line.OrderUnit.IsKilogram && item.Share.ActualKilograms is null)
                ? null
                : invoiced.Sum(item => line.OrderUnit.IsKilogram ? item.Share.Quantity : item.Share.ActualKilograms!.Value);
            var priceAmount = surcharged ? null : invoicedAmount;
            if (invoiced.Count > 0) actualDocument.Add(new(priceAmount, actualKg));
            var invoicedTheoreticalKg = invoiced.Count == 0 ? null : TheoreticalKg(invoicedQuantity, perUnit);
            decimal? difference = actualKg is { } a && invoicedTheoreticalKg is { } t ? a - t : null;
            // TK8: signed deviation = (actual − theoretical) / theoretical × 100, shown once rounded to 0,01 (URN-D7 display).
            string? deviation = difference is { } d && invoicedTheoreticalKg is { } basis && basis > 0m
                ? decimal.Round(d * 100m / basis, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)
                : null;

            lines.Add(new SalesOrderLineKilogramPricesV1(line.Id, line.LineNo, KgText(theoreticalKg),
                View(SalesKilogramPriceCalculator.Line(amount, theoreticalKg, SalesKilogramPriceState.TheoreticalKgUnavailable)),
                KgText(invoicedQuantity)!, invoicedAmount is { } value ? value.ToString("0.00", CultureInfo.InvariantCulture) : null,
                KgText(actualKg),
                View(SalesKilogramPriceCalculator.Line(priceAmount, actualKg, SalesKilogramPriceState.WeighedKgUnavailable)),
                difference is { } signed ? KgText(signed) : null, deviation));
        }

        return new SalesOrderKilogramPricesResponse(SalesKilogramPriceStatesV1.Computed, draft.Header.CurrencyId, lines,
            View(SalesKilogramPriceCalculator.Document(theoreticalDocument)),
            actualDocument.Count == 0
                ? View(SalesKilogramPrice.Unavailable(SalesKilogramPriceState.WeighedKgUnavailable))
                : View(SalesKilogramPriceCalculator.Document(actualDocument)));
    }

    /// <summary>TK2 / URN-D9: the document kg of a quantity — quantity × kg/unit at 3 decimals, half away from zero.</summary>
    private static decimal? TheoreticalKg(decimal quantity, decimal? perUnit) =>
        perUnit is { } k ? decimal.Round(quantity * k, SalesInvoice.KilogramInformationScale, MidpointRounding.AwayFromZero) : null;

    private static string? KgText(decimal? value) =>
        value is { } kg ? (kg / 1.000000000000000000000000000m).ToString(CultureInfo.InvariantCulture) : null;

    internal static SalesKilogramPriceViewV1 View(SalesKilogramPrice price) => new(price.State.ToString(),
        price.Value is { } value ? value.ToString("0.00", CultureInfo.InvariantCulture) : null);
}
