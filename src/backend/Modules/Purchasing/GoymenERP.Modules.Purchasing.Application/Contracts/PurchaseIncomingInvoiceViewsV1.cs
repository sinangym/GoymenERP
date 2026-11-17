using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record PurchaseInvoiceAmountsViewV1(string Net, string Vat, string Withholding, string Payable);
public sealed record PurchaseInvoiceLineViewV1(Guid Id, int LineNo, Guid ProductId, Guid UnitId, string Quantity,
    string ItemType, string LinePurpose, string VatPercent, PurchaseInvoiceAmountsViewV1 Amounts, PurchaseInvoiceAmountsViewV1? FunctionalAmounts);
public sealed record PurchaseInvoiceVarianceViewV1(Guid InvoiceLineId, string Kind, string Expected, string Actual);
public sealed record PurchaseIncomingInvoiceViewV1(Guid Id, Guid CompanyId, string Version, string Status,
    Guid SupplierPartyId, Guid CompanyAccountId, Guid CurrencyId, Guid FunctionalCurrencyId, int MoneyScale, int FunctionalScale,
    string? OfficialRate, string OfficialNumber, Guid Ettn, DateOnly InvoiceDate, Guid DocumentId, string DocumentVersion,
    PurchaseInvoiceAmountsViewV1 Amounts, PurchaseInvoiceAmountsViewV1? FunctionalAmounts,
    IReadOnlyList<PurchaseInvoiceLineViewV1> Lines, IReadOnlyList<PurchaseInvoiceVarianceViewV1> Variances,
    PurchaseInvoiceVarianceDecision? VarianceDecision, string? FinalizedVersion);
public sealed record PurchaseIncomingInvoiceCommandResponseV1(PurchaseIncomingInvoiceViewV1 Invoice,
    bool IsReplay, string OriginalCorrelationId, string? FinanceOutcome);

public static class PurchaseIncomingInvoiceWireV1
{
    public const string Path = "/api/v1/purchasing/invoices";
    public static PurchaseIncomingInvoiceViewV1 View(PurchaseIncomingInvoiceSnapshot snapshot)
    {
        var invoice = snapshot.Restore();
        return new(invoice.Id, invoice.CompanyId, Text(invoice.Version), invoice.Status.ToString(), invoice.SupplierPartyId,
            invoice.CompanyAccountId, invoice.CurrencyId, invoice.FunctionalCurrencyId, invoice.MoneyScale, invoice.FunctionalScale,
            invoice.OfficialRate is { } rate ? Text(rate) : null, invoice.Source.OfficialNumber, invoice.Source.Ettn,
            invoice.Source.InvoiceDate, invoice.Source.DocumentId, Text(invoice.Source.DocumentVersion), Amounts(invoice.Amounts, invoice.MoneyScale),
            invoice.FunctionalAmounts is { } functional ? Amounts(functional, invoice.FunctionalScale) : null,
            invoice.Lines.Select(line => new PurchaseInvoiceLineViewV1(line.Id, line.LineNo, line.ProductId, line.UnitId, Text(line.Quantity),
                line.ItemType, line.LinePurpose, Text(line.VatPercent), Amounts(line.Amounts, invoice.MoneyScale),
                line.FunctionalAmounts is { } amounts ? Amounts(amounts, invoice.FunctionalScale) : null)).ToArray(),
            invoice.Variances.Select(item => new PurchaseInvoiceVarianceViewV1(item.InvoiceLineId, item.Kind, Text(item.Expected), Text(item.Actual))).ToArray(),
            invoice.VarianceDecision, invoice.FinalizedVersion is { } version ? Text(version) : null);
    }
    public static PurchaseIncomingInvoiceCommandResponseV1 Response(PurchaseIncomingInvoiceOperationResultV1 result) =>
        new(View(result.Invoice), result.IsReplay, result.OriginalCorrelationId, result.FinanceOutcome);
    public static PurchaseIncomingInvoiceVersionRequestV1 ReadVersion(string json)
    {
        var result = JsonSerializer.Deserialize<PurchaseIncomingInvoiceVersionRequestV1>(json, PurchasingJson.Options)
            ?? throw new JsonException("Request is required.");
        Version(result.ExpectedVersion); return result;
    }
    public static PurchaseIncomingInvoiceVarianceApprovalRequestV1 ReadApproval(string json)
    {
        var result = JsonSerializer.Deserialize<PurchaseIncomingInvoiceVarianceApprovalRequestV1>(json, PurchasingJson.Options)
            ?? throw new JsonException("Request is required.");
        Version(result.ExpectedVersion);
        if (string.IsNullOrWhiteSpace(result.Reason) || result.Reason.Length > 2000) throw new JsonException("Reason is required.");
        return result;
    }
    private static void Version(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
            || value != version.ToString(CultureInfo.InvariantCulture))
            throw new JsonException("Version is required.");
    }
    private static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static PurchaseInvoiceAmountsViewV1 Amounts(PurchaseInvoiceAmounts amounts, int scale) =>
        new(Money(amounts.Net, scale), Money(amounts.Vat, scale), Money(amounts.Withholding, scale), Money(amounts.Payable, scale));

    private static string Money(decimal amount, int scale) => amount.ToString("F" + scale, CultureInfo.InvariantCulture);
}
