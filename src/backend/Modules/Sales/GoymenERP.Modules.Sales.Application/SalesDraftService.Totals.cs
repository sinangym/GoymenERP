using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-03 / SAT-D11 / delegation No 15: the order's line and document totals, read-only (no attempt, audit or version effect;
/// nothing cached or stored). Every amount is <see cref="SalesOrderTotalsCalculator"/>'s answer from the stored lines and the
/// stored money profile snapshot — the screen, the risk read (SAT-D46) and reports never recompute.
/// <list type="bullet">
/// <item><b>Authority:</b> the draft read (<c>sales.drafts.read</c>) and the price read (<c>sales.orders.price.read</c>): the
/// totals ARE prices, so without the price read nothing is disclosed (403), as on the line pricing. The price read is measured
/// before the draft is read (no draft is touched for a caller who may not see its prices) and again after it, as the final
/// await; the draft read is re-measured after the store read (the fence of every Sales read).</item>
/// <item><b>Company:</b> another company's draft is null under the tenant filter: 404, never 403.</item>
/// <item><b>Tax (MOD-03 → Sales):</b> each line's tax snapshot as the Settings line tax port resolved it (SAT-D139 item type default,
/// SAT-D140/D147/D158 the order's buyer situation, SAT-D93/D97 a reasoned line change) — the calculator applies it per line
/// (SAT-D75 withholding from the rounded line VAT; SAT-D76 the unit price never rounded). A line without a usable snapshot is
/// <c>NoSource</c> with its reason (e.g. <c>item_type_default_missing</c>, <c>tax_snapshot_missing</c>) and the document's VAT,
/// withholding, gross and payable stay empty — the net amounts that CAN be computed are. Nothing is defaulted.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftTotalsResponse> GetDraftTotalsAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        await RequirePriceDisclosureAsync(scope, cancellationToken);
        var draft = await LoadDraftAsync(scope, bindings.ReadPermission, draftId, cancellationToken);
        await RequirePriceDisclosureAsync(scope, cancellationToken);

        // MOD-03 → Sales: the lines' own tax snapshots through the one calculator (never a rate of this read's own).
        return Wire(draft, SalesOrderTotalsCalculator.CalculateWithLineTax(draft));
    }

    private async Task RequirePriceDisclosureAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    private static SalesOrderDraftTotalsResponse Wire(SalesOrderDraft draft, SalesOrderTotals totals)
    {
        var quantities = draft.Lines.ToDictionary(line => line.Id, line => line.QuantityText);
        var orderUnits = draft.Lines.ToDictionary(line => line.Id, line => line.OrderUnit.UnitId);
        return new SalesOrderDraftTotalsResponse(
            draft.Version.ToString(CultureInfo.InvariantCulture),
            SalesDraftApiV1.RevisionText(draft.Revision),
            totals.CurrencyId,
            totals.AmountScale,
            TaxStateText(totals.TaxState),
            WireDocument(totals.Document),
            [.. totals.Lines.Select(line => WireLine(line, orderUnits[line.LineId], quantities[line.LineId]))]);
    }

    /// <summary>The document totals as the totals read and the order PDF (MOD-07) answer them.</summary>
    private static SalesDraftDocumentTotalsV1 WireDocument(SalesDocumentTotal document) => new(
        DocumentStateText(document.State),
        document.UnpricedLineCount,
        document.PriceUnitNotOrderUnitLineCount,
        document.AmountOutOfRangeLineCount,
        Amount(document.NetAmount),
        TaxStateText(document.TaxState),
        Amount(document.VatAmount),
        Amount(document.WithheldVatAmount),
        Amount(document.GrossAmount),
        Amount(document.PayableAmount),
        document.TaxUnavailableLineCount);

    /// <summary>One line's totals as the totals read and the order PDF (MOD-07) answer them.</summary>
    private static SalesDraftLineTotalV1 WireLine(SalesLineTotal line, Guid orderUnitId, string quantity) => new(
        line.LineId,
        line.LineNo,
        orderUnitId,
        quantity,
        LineStateText(line.State),
        line.NetUnitPrice is { } net ? SalesLinePriceCalculator.Format(net) : null,
        Amount(line.NetAmount),
        TaxStateText(line.TaxState),
        line.TaxRate is { } rate ? SalesLinePriceCalculator.Format(rate.VatPercent) : null,
        Amount(line.VatAmount),
        line.TaxRate?.WithholdingShare is { } share ? SalesLinePriceCalculator.Format(share) : null,
        Amount(line.WithheldVatAmount),
        Amount(line.GrossAmount),
        Amount(line.PayableAmount),
        line.TaxState == SalesTaxState.NoSource ? line.TaxReason : null);

    /// <summary>The amount padded to its scale in the invariant culture ("1250.00" at 2); null stays null.</summary>
    private static string? Amount(Money? money) => money is { } value
        ? value.Amount.ToString("F" + value.Scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
        : null;

    private static string LineStateText(SalesLineTotalState state) => state switch
    {
        SalesLineTotalState.Computed => SalesDraftTotalsStatesV1.Computed,
        SalesLineTotalState.Unpriced => SalesDraftTotalsStatesV1.Unpriced,
        SalesLineTotalState.PriceUnitNotOrderUnit => SalesDraftTotalsStatesV1.PriceUnitNotOrderUnit,
        SalesLineTotalState.AmountOutOfRange => SalesDraftTotalsStatesV1.AmountOutOfRange,
        _ => throw new InvalidOperationException($"Unknown line total state {state}."),
    };

    private static string DocumentStateText(SalesDocumentTotalState state) => state switch
    {
        SalesDocumentTotalState.Computed => SalesDraftTotalsStatesV1.Computed,
        SalesDocumentTotalState.Incomplete => SalesDraftTotalsStatesV1.Incomplete,
        SalesDocumentTotalState.AmountOutOfRange => SalesDraftTotalsStatesV1.AmountOutOfRange,
        _ => throw new InvalidOperationException($"Unknown document total state {state}."),
    };

    private static string TaxStateText(SalesTaxState state) => state switch
    {
        SalesTaxState.Computed => SalesDraftTotalsStatesV1.Computed,
        SalesTaxState.NoSource => SalesDraftTotalsStatesV1.NoSource,
        SalesTaxState.NotComputed => SalesDraftTotalsStatesV1.NotComputed,
        _ => throw new InvalidOperationException($"Unknown tax state {state}."),
    };
}
