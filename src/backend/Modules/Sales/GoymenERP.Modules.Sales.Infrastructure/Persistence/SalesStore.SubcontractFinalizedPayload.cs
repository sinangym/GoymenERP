using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    // Same versioned event as dispatch invoices; all amounts are frozen calculator values.
    internal static string FinalizedSubcontractPayload(SalesSubcontractServiceInvoice invoice, SalesInvoiceFinalization finalization)
    {
        var totals = invoice.Totals;
        var functionalTotals = invoice.FunctionalTotals;
        return JsonSerializer.Serialize(new
        {
            invoiceId = invoice.Id,
            officialInvoiceNo = invoice.Match!.OfficialNo,
            invoice.DocumentNo,
            version = invoice.Version,
            finalization.AdmissionId,
            invoice.Header.PartyId,
            invoice.Header.CompanyAccountId,
            invoice.Header.CurrencyId,
            rate = invoice.Rate?.Rate,
            payable = totals.Payable.Amount,
            functionalPayable = functionalTotals?.Payable.Amount,
            match = invoice.Match is null ? null : new
            {
                invoice.Match.OfficialNo,
                invoice.Match.Ettn,
                invoice.Match.OfficialFunctionalAmount,
                invoice.Match.Difference,
                invoice.Match.InvoiceDate,
                invoice.Match.SourceProof,
            },
            instalments = finalization.Plan.Select(item => new
            {
                item.OpenItemId,
                item.InstallmentId,
                item.SequenceNo,
                item.DueDate,
                item.Amount,
                item.FunctionalAmount,
            }).ToArray(),
            invoice.CompanyId,
            invoice.Header.BusinessDate,
            invoiceDate = invoice.Match!.InvoiceDate,
            finalization.FinalizedBy,
            rateDay = invoice.Rate?.RateDay,
            functionalCurrencyId = invoice.Rate?.FunctionalCurrencyId,
            net = totals.Net.Amount,
            vat = totals.Vat.Amount,
            withholding = totals.Withholding.Amount,
            functionalNet = functionalTotals?.Net.Amount,
            functionalVat = functionalTotals?.Vat.Amount,
            functionalWithholding = functionalTotals?.Withholding.Amount,
            lines = invoice.Lines.OrderBy(line => line.LineNo).Select(line => new
            {
                lineId = line.Id,
                line.LineNo,
                line.Input.ProductId,
                line.Input.UnitId,
                line.Input.Quantity,
                ItemType = "Service",
                line.Input.Tax.BuyerSituation,
                line.VatPercent,
                line.WithholdingShare,
                line.Input.Tax.ExemptionCode,
                line.Input.Tax.WithholdingCode,
                net = line.Amounts.Net.Amount,
                vat = line.Amounts.Vat.Amount,
                withholding = line.Amounts.Withholding.Amount,
                payable = line.Amounts.Payable.Amount,
                functionalNet = invoice.FunctionalLines.SingleOrDefault(item => item.LineId == line.Id)?.Net,
                functionalVat = invoice.FunctionalLines.SingleOrDefault(item => item.LineId == line.Id)?.Vat,
                functionalWithholding = invoice.FunctionalLines.SingleOrDefault(item => item.LineId == line.Id)?.Withholding,
                functionalPayable = invoice.FunctionalAmounts(line.Id)?.Payable.Amount,
            }).ToArray(),
        }, Json);
    }
}
