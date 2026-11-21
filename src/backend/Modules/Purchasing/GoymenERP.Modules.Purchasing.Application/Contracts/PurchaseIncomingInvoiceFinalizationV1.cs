using GoymenERP.Modules.Purchasing.Domain.Invoices;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>ALS-D41/D59/D64, FNS-D105: one immutable Purchasing source feeds Finance and Accounting.
/// Creating this payload does not acknowledge Finance, write its tables, or send the outbox.</summary>
public static class PurchaseIncomingInvoiceFinalizationV1
{
    public static PurchaseInvoiceFinalizedPayloadV1 Event(PurchaseIncomingInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.Status is not (PurchaseIncomingInvoiceStatus.FinancePending or PurchaseIncomingInvoiceStatus.FinanceAccepted)
            || invoice.AdmissionId is not { } admission || invoice.FinalizedVersion is not { } finalizedVersion
            || invoice.FinalizedBy is null || invoice.FinalizedAtUtc is null)
            throw new InvalidOperationException("Fatura kesinleşmeden olay veya Finans kabulü üretilemez.");
        return new(invoice.Id, finalizedVersion, admission, invoice.SupplierPartyId, invoice.CurrencyId, invoice.OfficialRate,
            invoice.Amounts.Payable, invoice.FunctionalAmounts?.Payable, invoice.CompanyId, invoice.Amounts.Net,
            invoice.Amounts.Vat, invoice.Amounts.Withholding, invoice.FunctionalAmounts?.Net, invoice.FunctionalAmounts?.Vat,
            invoice.FunctionalAmounts?.Withholding, Array.AsReadOnly(invoice.Lines.Select(line => new PurchaseInvoiceFinalizedLineV1(
                line.Id, line.LineNo, line.ItemType, line.LinePurpose, line.VatPercent, line.Amounts.Net, line.Amounts.Vat,
                line.Amounts.Withholding, line.FunctionalAmounts?.Net, line.FunctionalAmounts?.Vat, line.FunctionalAmounts?.Withholding,
                line.ServiceTypeId, line.ExpenseTypeId, TaxSources(invoice, line.Id), GoodsCostSource(invoice, line))).ToArray()),
            invoice.Source.InvoiceDate, invoice.FinalizedBy, invoice.Installments.Min(item => item.DueOn), invoice.Source.OfficialNumber,
            new(invoice.Source.DocumentId, invoice.Source.DocumentVersion, invoice.Source.Sha256, invoice.Source.Ettn));
    }

    private static PurchaseInvoiceGoodsCostSourceV1? GoodsCostSource(PurchaseIncomingInvoice invoice, PurchaseInvoiceLine line)
    {
        if (line.ProductId == Guid.Empty || line.ServiceTypeId is not null || line.ExpenseTypeId is not null
            || line.LinePurpose is "service" or "expense") return null;
        var matches = invoice.Matches.Where(x => x.InvoiceLineId == line.Id).OrderBy(x => x.AcceptanceId).ToArray();
        // Historical matches may predate the owner quantity proof. Never fill their gap from current rows.
        if (matches.Length == 0 || matches.Any(x => x.AcceptedQuantity is not > 0)) return null;
        return new(line.ProductId, line.UnitId, line.Quantity, Array.AsReadOnly(matches.Select(x =>
            new PurchaseInvoiceReceiptCostShareV1(x.AcceptanceId, x.OrderId, x.OrderLineId, x.OrderVersion, x.Quantity, x.AcceptedQuantity!.Value,
                x.ReceiptAdjustments is null ? null : Array.AsReadOnly(x.ReceiptAdjustments.Select(adjustment =>
                    new PurchaseInvoiceReceiptAdjustmentV1(adjustment.AdjustmentId, adjustment.UndoQuantity, adjustment.IsSupplierReturn)).ToArray()))).ToArray()));
    }

    private static IReadOnlyList<PurchaseInvoiceSubcontractTaxSourceV1>? TaxSources(PurchaseIncomingInvoice invoice, Guid lineId)
    {
        var sources = invoice.Matches.Where(x => x.InvoiceLineId == lineId && x.SubcontractTax is not null)
            .Select(x => new PurchaseInvoiceSubcontractTaxSourceV1(x.OrderId, x.OrderVersion, x.AcceptanceId, x.SubcontractTax!)).ToArray();
        return sources.Length == 0 ? null : Array.AsReadOnly(sources);
    }

    /// <summary>Technical stable retry identity; independent of an HTTP retry key and never recomputed from the current version.</summary>
    public static string AdmissionKey(PurchaseIncomingInvoice invoice)
    {
        var payload = Event(invoice);
        return FormattableString.Invariant($"purchasing-invoice:{payload.InvoiceId:D}:{payload.Version}");
    }

    public static PurchaseInvoiceFinanceAdmissionRequestV1 Finance(PurchaseIncomingInvoice invoice,
        string actorId, string correlationId, string? causationId)
    {
        _ = Event(invoice); // Validates the frozen finalisation, never the acknowledgement version.
        if (string.IsNullOrWhiteSpace(actorId) || actorId != actorId.Trim()
            || string.IsNullOrWhiteSpace(correlationId) || correlationId != correlationId.Trim())
            throw new ArgumentException("Finans kabulünün aktörü ve correlation bilgisi gereklidir.");
        // A different authorised retry actor uses another transport key, while the SAME AdmissionId preserves
        // Finance's source dedup. Never impersonate the original finaliser or change invoice/instalment identities.
        var actorHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(actorId)));
        var key = $"purchasing-invoice:{invoice.AdmissionId!.Value:N}:{actorHash}";
        return new(invoice.CompanyId, actorId, correlationId, causationId, key, invoice.AdmissionId.Value, invoice.Id,
            invoice.FinalizedVersion!.Value.ToString(CultureInfo.InvariantCulture), invoice.SupplierPartyId,
            invoice.CompanyAccountId, invoice.CurrencyId, invoice.OfficialRate?.ToString("0.############################", CultureInfo.InvariantCulture),
            Array.AsReadOnly(invoice.Installments.Select(item => new PurchaseInvoiceFinanceInstallmentV1(item.OpenItemId, item.Id,
                item.Sequence, item.DueOn, Text(item.Amount, invoice.MoneyScale),
                item.FunctionalAmount is { } functional ? Text(functional, invoice.FunctionalScale) : null)).ToArray()));

        static string Text(decimal value, int scale) => value.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
