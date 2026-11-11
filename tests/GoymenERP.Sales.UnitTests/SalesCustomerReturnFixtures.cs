using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-40 test data: finalised SENTETIK invoices rebuilt through <see cref="SalesInvoice.Restore"/> with exactly the stored amounts a
/// golden needs (the invoice calculator is not re-run), and finalised prior returns.
/// </summary>
internal static class SalesCustomerReturnFixtures
{
    public static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid Usd = Guid.Parse("77777777-7777-4777-8777-777777777777");
    public static readonly Guid Try = Guid.Parse("55555555-5555-4555-8555-555555555555");
    public static readonly Guid Kg = Guid.Parse("66666666-6666-4666-8666-666666666666");
    public static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    public static readonly DateOnly Day = new(2026, 11, 7);
    public static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    public const string Preparer = "SENTETIK-preparer";
    public const string Approver = "SENTETIK-approver";

    /// <summary>One stored invoice line: quantity, the invoice-currency net/VAT/withholding and optionally the stored TL ones.</summary>
    public sealed record StoredLine(decimal Quantity, decimal Net, decimal Vat, decimal Withholding, decimal NetUnitPrice = 1m,
        decimal VatPercent = 20m, decimal? WithholdingShare = null, Guid? ShipmentId = null, (decimal Net, decimal Vat, decimal Withholding)? Tl = null);

    public static Money M(decimal amount, Guid? currency = null) => Money.Of(amount, currency ?? Try, 2);

    public static Guid LineId(int lineNo) => Guid.Parse($"aaaaaaaa-aaaa-4aaa-8aaa-{lineNo:D12}");

    public static Guid ShipmentOf(int index) => Guid.Parse($"bbbbbbbb-bbbb-4bbb-8bbb-{index:D12}");

    public static SalesInvoice TlInvoice(params StoredLine[] lines) => Invoice(Try, null, SalesInvoiceState.Finalized, lines);

    public static SalesInvoice UsdInvoice(decimal rate, params StoredLine[] lines) => Invoice(Usd, rate, SalesInvoiceState.Finalized, lines);

    public static SalesInvoice Invoice(Guid currency, decimal? rate, SalesInvoiceState state, params StoredLine[] lines)
    {
        var tax = new SalesLineTaxSnapshot(Product, Day, "Normal", "Steel", "Resolved", null, "Record",
            Guid.Parse("12121212-1212-4212-8212-121212121212"), 1, "Resolved", 20m, "NotRequired", null, null, "NotApplied", null,
            "NotRequired", null, null, null, 0, "Normal", false);
        var stored = new List<SalesInvoiceLine>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var share = new SalesInvoiceShareInput(LineId(index + 1), line.ShipmentId ?? ShipmentOf(index + 1),
                Guid.Parse($"cccccccc-cccc-4ccc-8ccc-{index + 1:D12}"), $"IRS-SENTETIK-{index + 1}", 1, Day,
                Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"), Guid.Parse($"eeeeeeee-eeee-4eee-8eee-{index + 1:D12}"), 0, Product, Kg,
                line.Quantity, line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new CommercialDraftFields("1", Kg, null), tax);
            var amounts = SalesInvoiceCalculator.Set(M(line.Net, currency), M(line.Vat, currency), M(line.Withholding, currency));
            SalesInvoiceFunctionalLine? functional = line.Tl is { } tl
                ? new SalesInvoiceFunctionalLine(SalesInvoiceCalculator.Set(M(tl.Net), M(tl.Vat), M(tl.Withholding)), [], false)
                : null;
            stored.Add(SalesInvoice.StoredLine(index + 1, share, line.NetUnitPrice, line.VatPercent, line.WithholdingShare, amounts, functional));
        }

        var payable = stored.Aggregate(Money.Zero(currency, 2), (sum, line) => sum.Add(line.Amounts.Payable));
        var header = new SalesInvoiceHeader(Guid.Parse("f0f0f0f0-f0f0-4f0f-8f0f-f0f0f0f0f0f0"), Guid.Parse("f1f1f1f1-f1f1-4f1f-8f1f-f1f1f1f1f1f1"),
            currency, Guid.Parse("f2f2f2f2-f2f2-4f2f-8f2f-f2f2f2f2f2f2"), "1", 2, Day,
            rate is { } value ? new SalesInvoiceRateSnapshot(value, Day, Day.AddDays(-1), Try, 2) : null);
        var facts = state == SalesInvoiceState.Finalized || state == SalesInvoiceState.Approved
            ? new SalesInvoiceDecisionFacts(Preparer, Now, Approver, Now, null, null, null, null)
            : SalesInvoiceDecisionFacts.None;
        return SalesInvoice.Restore(Guid.Parse("abababab-abab-4bab-8bab-abababababab"), Company, "SF-SENTETIK-1", header, state,
            state == SalesInvoiceState.Finalized ? 5 : 1, Preparer, Now, stored, [new SalesInvoiceInstalment(1, Day.AddDays(30), "100", payable)],
            facts);
    }

    /// <summary>A finalised prior return of a line, as its ledger entry holds it.</summary>
    public static SalesCustomerReturnPrior Prior(SalesCustomerReturnLineResult result, bool linked = true) =>
        new(result.Quantity, result.Amounts, result.Functional, linked);

    public static string[] Text(SalesCustomerReturnAmounts amounts) =>
        [F(amounts.Net), F(amounts.Vat), F(amounts.Withholding), F(amounts.LineTotal)];

    public static string F(Money money) => money.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
