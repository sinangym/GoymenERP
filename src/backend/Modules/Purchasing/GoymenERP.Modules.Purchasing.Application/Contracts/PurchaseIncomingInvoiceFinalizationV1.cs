using GoymenERP.Modules.Purchasing.Domain.Invoices;

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
                line.Amounts.Withholding, line.FunctionalAmounts?.Net, line.FunctionalAmounts?.Vat, line.FunctionalAmounts?.Withholding)).ToArray()),
            invoice.Source.InvoiceDate, invoice.FinalizedBy, invoice.Installments.Min(item => item.DueOn));
    }

    /// <summary>Technical stable retry identity; independent of an HTTP retry key and never recomputed from the current version.</summary>
    public static string AdmissionKey(PurchaseIncomingInvoice invoice)
    {
        var payload = Event(invoice);
        return FormattableString.Invariant($"purchasing-invoice:{payload.InvoiceId:D}:{payload.Version}");
    }
}
