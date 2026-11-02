using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// MOD-03 → Sales: the line tax snapshot's reading by the one calculator (SAT-D73…D76, D139, D147), the buyer tax shape
/// (SAT-D140/D147), the stored JSON (strict both ways) and the migration's rollback refusal. Every value is SENTETIK.
/// </summary>
public sealed class SalesLineTaxTests
{
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Kg = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Try = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateOnly Day = new(2026, 11, 2);

    private static SalesLineTaxSnapshot Snapshot(string state = "Resolved", string? vatState = "Resolved", decimal? vat = 20m,
        string? withholdingState = "Resolved", decimal? withheld = 50m, string? reason = null, SalesLineTaxOverride? change = null) =>
        new(Product, Day, "Normal", "Steel", state, reason, "Record", Guid.Parse("12121212-1212-4212-8212-121212121212"), 1, vatState, vat,
            "NotRequired", null, null, withholdingState, withheld, withholdingState == "Resolved" ? "Unresolved" : "NotRequired", null,
            withholdingState == "Resolved" ? "withholding_code_missing" : null, null, 0, "Normal", false, change);

    [Fact]
    public void A_resolved_snapshot_gives_the_vat_percent_and_the_withheld_share_to_the_calculator()
    {
        Assert.Equal(SalesLineTaxRate.Of(20m, 0.5m), Snapshot().Rate);
        Assert.Equal(SalesLineTaxRate.Of(20m, null), Snapshot(withholdingState: "NotApplied", withheld: null).Rate);
        // "KDV yok" (SAT-D139 hurda) and KDV %0 (SAT-D147) both apply 0 — never "no tax source".
        Assert.Equal(SalesLineTaxRate.Of(0m, null), Snapshot(vatState: "Exempt", vat: null, withholdingState: "NotApplied", withheld: null).Rate);
        Assert.Equal(SalesLineTaxRate.Of(0m, null), Snapshot(vatState: "ZeroRated", vat: 0m, withholdingState: "NotApplied", withheld: null).Rate);
    }

    [Theory]
    [InlineData("Unresolved", null, "item_type_default_missing", "item_type_default_missing")]
    [InlineData("Unresolved", null, null, SalesLineTaxReasons.Unresolved)]
    [InlineData("Resolved", "Unresolved", null, SalesLineTaxReasons.RateIncomplete)]
    public void A_snapshot_without_a_usable_part_gives_no_rate_and_says_why(string state, string? vatState, string? reason, string expected)
    {
        var snapshot = Snapshot(state, vatState, null, null, null, reason);

        Assert.Null(snapshot.Rate);
        Assert.Equal(expected, snapshot.UnavailableReason);
    }

    [Fact]
    public void The_calculator_applies_each_lines_own_snapshot_and_a_line_without_one_is_no_source_with_its_reason()
    {
        var lines = new[]
        {
            // 10 × 100 = 1000.00; VAT 200.00; withheld 100.00 (SAT-D75, from the rounded VAT).
            new SalesTotalsLineInput(Guid.NewGuid(), 1, Kg, 10m, new CommercialDraftFields("100", Kg, null), Snapshot()),
            new SalesTotalsLineInput(Guid.NewGuid(), 2, Kg, 1m, new CommercialDraftFields("50", Kg, null)),
        };

        var totals = SalesOrderTotalsCalculator.CalculateWithLineTax(lines, Try, 2);

        Assert.Equal((SalesTaxState.Computed, 200m, 100m, 1100m), (totals.Lines[0].TaxState, totals.Lines[0].VatAmount!.Value.Amount,
            totals.Lines[0].WithheldVatAmount!.Value.Amount, totals.Lines[0].PayableAmount!.Value.Amount));
        Assert.Equal((SalesTaxState.NoSource, SalesLineTaxReasons.SnapshotMissing), (totals.Lines[1].TaxState, totals.Lines[1].TaxReason));
        Assert.Equal((SalesTaxState.NoSource, SalesTaxState.NoSource, 1050m, 1), (totals.TaxState, totals.Document.TaxState,
            totals.Document.NetAmount!.Value.Amount, totals.Document.TaxUnavailableLineCount));
        Assert.Null(totals.Document.GrossAmount);
    }

    [Fact]
    public void The_invoice_waits_for_an_unresolved_code_and_a_document_the_situation_requires()
    {
        var zero = Snapshot(vatState: "ZeroRated", vat: 0m, withholdingState: "NotApplied", withheld: null) with
        {
            ExemptionCodeState = "Unresolved", ExemptionCodeReason = "exemption_code_missing", BuyerDocumentRequired = true,
        };

        Assert.Equal(["exemption_code_missing", SalesLineTaxReasons.BuyerDocumentMissing],
            zero.InvoiceBlockers(SalesBuyerTax.Create("ExportRegistered")));
        Assert.Equal(["exemption_code_missing"], zero.InvoiceBlockers(SalesBuyerTax.Create("ExportRegistered", "701", "SENTETIK-1", Day)));
    }

    [Theory]
    [InlineData("", null, null, false, SalesRuleCodes.TaxBuyerSituationInvalid)]
    [InlineData("9Normal", null, null, false, SalesRuleCodes.TaxBuyerSituationInvalid)]
    [InlineData("Normal", "70-1", null, false, SalesRuleCodes.TaxExemptionCodeInvalid)]
    [InlineData("Normal", null, "SENTETIK-1", false, SalesRuleCodes.TaxBuyerDocumentIncomplete)]
    [InlineData("Normal", null, null, true, SalesRuleCodes.TaxBuyerDocumentIncomplete)]
    public void The_buyer_tax_shape_is_refused_with_its_code(string situation, string? code, string? number, bool date, string expected)
    {
        var refused = Assert.Throws<SalesRuleException>(() => SalesBuyerTax.Create(situation, code, number, date ? Day : null));
        Assert.Equal(expected, refused.Code);
    }

    [Fact]
    public void The_buyer_tax_normalizes_the_code_and_compares_by_value()
    {
        var buyer = SalesBuyerTax.Create("ExportRegistered", " 701 ", " SENTETIK-1 ", Day);

        Assert.Equal(("701", "SENTETIK-1", true), (buyer.ExemptionCode, buyer.DocumentNo, buyer.HasDocument));
        Assert.Equal(SalesBuyerTax.Create("ExportRegistered", "701", "SENTETIK-1", Day), buyer);
        Assert.NotEqual(SalesBuyerTax.Create("Normal"), buyer);
    }

    [Fact]
    public void The_stored_tax_round_trips_exactly_and_a_corrupt_row_is_refused_never_repaired()
    {
        var change = new SalesLineTaxOverride("Service", "FinalConsumer", null, "SENTETIK gerekçe", "synthetic-actor",
            new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero), VatRateCode: "10", WithholdingRatioCode: "2/10");
        var snapshot = Snapshot(change: change);
        var buyer = SalesBuyerTax.Create("ExportRegistered", "701", "SENTETIK-1", Day);

        Assert.Equal(snapshot, SalesTaxJson.RestoreLine(SalesTaxJson.SerializeLine(snapshot)));
        Assert.Equal(buyer, SalesTaxJson.RestoreBuyer(SalesTaxJson.SerializeBuyer(buyer)));
        Assert.Null(SalesTaxJson.SerializeLine(null));
        // SAT-D181: a change without list picks keeps the JSON written before them (no new members).
        var plain = Snapshot(change: change with { VatRateCode = null, WithholdingRatioCode = null });
        Assert.DoesNotContain("vatRateCode", SalesTaxJson.SerializeLine(plain), StringComparison.Ordinal);
        Assert.Equal(plain, SalesTaxJson.RestoreLine(SalesTaxJson.SerializeLine(plain)));
        Assert.Null(SalesTaxJson.RestoreBuyer(null));
        Assert.Throws<InvalidOperationException>(() => SalesTaxJson.RestoreLine("{\"unknown\":1}"));
        Assert.Throws<InvalidOperationException>(() => SalesTaxJson.RestoreBuyer("{\"situation\":\"not a code\",\"exemptionCode\":null,"
            + "\"documentNo\":null,\"documentDate\":null}"));
    }

    [Fact]
    public void The_order_tax_migration_down_refuses_with_51135_before_it_drops_anything()
    {
        var migration = new AddDraftOrderTax();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51135,", guard.Sql, StringComparison.Ordinal);
        foreach (var column in new[]
                 {
                     "[sales].[Drafts] WHERE [BuyerTaxJson] IS NOT NULL", "[sales].[DraftLines] WHERE [TaxJson] IS NOT NULL",
                     "[sales].[DraftRevisionHeaders] WHERE [BuyerTaxJson] IS NOT NULL", "[sales].[DraftRevisionLines] WHERE [TaxJson] IS NOT NULL",
                 })
        {
            Assert.Contains(column, guard.Sql, StringComparison.Ordinal);
        }

        Assert.All(migration.DownOperations.Skip(1), operation => Assert.IsNotType<SqlOperation>(operation));
        Assert.Equal(4, migration.UpOperations.OfType<AddColumnOperation>().Count(column => column.IsNullable
            && column.Name is "BuyerTaxJson" or "TaxJson"));
    }

    [Fact]
    public void The_buyer_tax_backfill_writes_normal_once_in_the_stores_exact_json_and_its_down_is_a_documented_no_op()
    {
        var migration = new BackfillDraftBuyerTax();
        var backfill = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations));
        Assert.Contains($"SET [BuyerTaxJson] = N'{SalesTaxJson.SerializeBuyer(SalesBuyerTax.Create("Normal"))}'", backfill.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [BuyerTaxJson] IS NULL", backfill.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.DownOperations);
    }
}
