using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.BuildingBlocks.Multitenancy;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// S5 API-32 (Fatura çekirdeği 1): the invoice calculator's goldens — invoice-currency amounts as the order calculator (SAT-D11,
/// SAT-D75, SAT-D76) and the TL equivalent of EK42 (SAT-D223 per VAT rate group with the difference on the group's last line
/// carrying that amount type, SAT-D224 withholding × rate, SAT-D225 payable = net + VAT − withholding, SAT-D226 the invoice's
/// rate), the "işaret/aşma" flag, the fail-closed states, the draft aggregate and the persistence model. Every value is SENTETIK.
/// </summary>
public sealed class SalesInvoiceCalculatorTests
{
    private static readonly Guid Usd = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Try = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Kg = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateOnly Day = new(2026, 11, 7);

    private static SalesLineTaxSnapshot Tax(string vatState = "Resolved", decimal? vat = 20m, decimal? withheld = null) =>
        new(Product, Day, "Normal", "Steel", "Resolved", null, "Record", Guid.Parse("12121212-1212-4212-8212-121212121212"), 1, vatState,
            vat, "NotRequired", null, null, withheld is null ? "NotApplied" : "Resolved", withheld, "NotRequired", null, null, null, 0,
            "Normal", false);

    private static SalesInvoiceLineInput Line(int lineNo, string quantity, string? price, SalesLineTaxSnapshot? tax) =>
        new(Guid.Parse($"aaaaaaaa-aaaa-4aaa-8aaa-{lineNo:D12}"), lineNo, Kg, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
            new CommercialDraftFields(price, price is null ? null : Kg, null), tax);

    private static SalesInvoiceFunctionalConversion Rate(decimal rate) => new(ExchangeRate.Of(Usd, Try, rate), 2);

    private static string Text(Money money) => money.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static string[] Set(SalesInvoiceAmountSet set) =>
        [Text(set.Net), Text(set.Vat), Text(set.Withholding), Text(set.Gross), Text(set.Payable)];

    [Fact]
    public void A_foreign_currency_invoice_converts_each_vat_group_and_puts_the_kurus_difference_on_its_last_line()
    {
        var result = SalesInvoiceCalculator.Calculate(
        [
            Line(1, "3", "33.33", Tax(vat: 20m)),
            Line(2, "1", "10.01", Tax(vat: 20m)),
            Line(3, "2", "7.77", Tax(vat: 10m)),
            Line(4, "1", "100.00", Tax(vatState: "Exempt", vat: null)),
        ], Usd, 2, Rate(34.5678m));

        Assert.Equal(SalesInvoiceAmountsState.Computed, result.State);
        Assert.Equal(["225.54", "23.55", "0.00", "249.09", "249.09"], Set(result.Totals!));
        // Group 20 %: net 110.00 × 34.5678 = 3802.46 but lines give 3456.43 + 346.02 = 3802.45 → +0.01 on line 2;
        // VAT 22.00 × rate = 760.49 but lines give 691.36 + 69.14 = 760.50 → −0.01 on line 2.
        Assert.Equal(["3456.43", "691.36", "0.00", "4147.79", "4147.79"], Set(result.Lines[0].Functional!.Amounts));
        Assert.Equal(["346.03", "69.13", "0.00", "415.16", "415.16"], Set(result.Lines[1].Functional!.Amounts));
        Assert.Equal([SalesInvoiceAmountType.Net, SalesInvoiceAmountType.Vat], result.Lines[1].Functional!.Adjusted);
        Assert.Empty(result.Lines[0].Functional!.Adjusted);
        Assert.Equal(["537.18", "53.58", "0.00", "590.76", "590.76"], Set(result.Lines[2].Functional!.Amounts));
        Assert.Equal(["3456.78", "0.00", "0.00", "3456.78", "3456.78"], Set(result.Lines[3].Functional!.Amounts));
        Assert.Equal([20m, 10m, 0m], result.Groups.Select(group => group.VatPercent));
        Assert.Equal(["3802.46", "760.49", "0.00", "4562.95", "4562.95"], Set(result.Groups[0].Functional!));
        // The document TL totals are the sums of the groups (SAT-D223), payable = net + VAT − withholding (SAT-D225).
        Assert.Equal(["7796.42", "814.07", "0.00", "8610.49", "8610.49"], Set(result.FunctionalTotals!));
        Assert.All(result.Lines, line => Assert.False(line.Functional!.NeedsReview));
    }

    [Fact]
    public void Withholding_is_converted_itself_and_its_difference_closes_inside_the_vat_group()
    {
        // SAT-D224: two lines of 0.10 at 20 % with 50 % withholding (0.01 each) at 1.5555; group withholding 0.02 → 0.03 TL while
        // the lines give 0.02 + 0.02 → −0.01 on the last withholding line; the net difference goes to the same last line.
        var result = SalesInvoiceCalculator.Calculate(
            [Line(1, "1", "0.10", Tax(vat: 20m, withheld: 50m)), Line(2, "1", "0.10", Tax(vat: 20m, withheld: 50m))], Usd, 2, Rate(1.5555m));

        Assert.Equal(["0.20", "0.04", "0.02", "0.24", "0.22"], Set(result.Totals!));
        Assert.Equal(["0.16", "0.03", "0.02", "0.19", "0.17"], Set(result.Lines[0].Functional!.Amounts));
        Assert.Equal(["0.15", "0.03", "0.01", "0.18", "0.17"], Set(result.Lines[1].Functional!.Amounts));
        Assert.Equal([SalesInvoiceAmountType.Net, SalesInvoiceAmountType.Withholding], result.Lines[1].Functional!.Adjusted);
        Assert.Equal(["0.31", "0.06", "0.03", "0.37", "0.34"], Set(result.FunctionalTotals!));
    }

    [Fact]
    public void A_difference_that_exceeds_or_flips_the_last_line_is_applied_and_flagged_never_refused()
    {
        // Seven lines of 0.01 at 1.5: each 0.015 → 0.02 TL (sum 0.14) while the group 0.07 → 0.11; −0.03 on the last line → −0.01.
        var lines = Enumerable.Range(1, 7).Select(lineNo => Line(lineNo, "1", "0.01", Tax(vatState: "Exempt", vat: null))).ToList();
        var result = SalesInvoiceCalculator.Calculate(lines, Usd, 2, Rate(1.5m));

        Assert.Equal(SalesInvoiceAmountsState.Computed, result.State);
        Assert.Equal("-0.01", Text(result.Lines[6].Functional!.Amounts.Net));
        Assert.True(result.Lines[6].Functional!.NeedsReview);
        Assert.All(result.Lines.Take(6), line => Assert.False(line.Functional!.NeedsReview));
        Assert.Equal("0.11", Text(result.FunctionalTotals!.Net));
    }

    [Fact]
    public void A_turkish_lira_invoice_has_no_conversion_and_its_totals_are_the_line_sums()
    {
        var result = SalesInvoiceCalculator.Calculate([Line(1, "2.5", "100.004", Tax(vat: 20m))], Try, 2, null);

        Assert.Equal(["250.01", "50.00", "0.00", "300.01", "300.01"], Set(result.Totals!));
        Assert.Null(result.FunctionalTotals);
        Assert.Null(result.Lines[0].Functional);
        Assert.Null(result.Groups[0].Functional);
    }

    [Fact]
    public void A_discounted_line_uses_the_unrounded_net_unit_price_and_rounds_only_the_line_amount()
    {
        // SAT-D76: 33.33 less 10 % = 29.997 (not rounded) x 3 kg = 89.991 -> 89.99; VAT 20 % = 17.998 -> 18.00 (SAT-D11).
        var discounted = new SalesInvoiceLineInput(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-000000000001"), 1, Kg, 3m,
            new CommercialDraftFields("33.33", Kg, "10"), Tax(vat: 20m));

        var result = SalesInvoiceCalculator.Calculate([discounted], Try, 2, null);

        Assert.Equal(["89.99", "18.00", "0.00", "107.99", "107.99"], Set(result.Totals!));
        Assert.Equal(29.997m, result.Lines[0].NetUnitPrice);
    }

    [Fact]
    public void A_zero_amount_line_stays_zero_and_never_takes_the_group_difference_in_a_foreign_currency_invoice()
    {
        // The EK42 golden group (99.99 + 10.01 at 20 %, rate 34.5678) plus a trailing zero-priced line of the same group: the
        // difference stays on the last line that CARRIES an amount (line 2), never on the zero line (EK42 "o tutar turunu tasiyan son satir").
        var result = SalesInvoiceCalculator.Calculate(
        [
            Line(1, "3", "33.33", Tax(vat: 20m)),
            Line(2, "1", "10.01", Tax(vat: 20m)),
            Line(3, "5", "0", Tax(vat: 20m)),
        ], Usd, 2, Rate(34.5678m));

        Assert.Equal(SalesInvoiceAmountsState.Computed, result.State);
        Assert.Equal(["0.00", "0.00", "0.00", "0.00", "0.00"], Set(result.Lines[2].Amounts));
        Assert.Equal(["0.00", "0.00", "0.00", "0.00", "0.00"], Set(result.Lines[2].Functional!.Amounts));
        Assert.Empty(result.Lines[2].Functional!.Adjusted);
        Assert.False(result.Lines[2].Functional!.NeedsReview);
        Assert.Equal([SalesInvoiceAmountType.Net, SalesInvoiceAmountType.Vat], result.Lines[1].Functional!.Adjusted);
        Assert.Equal(["3802.46", "760.49", "0.00", "4562.95", "4562.95"], Set(result.Groups[0].Functional!));
    }

    [Fact]
    public void An_unpriced_line_or_a_line_without_a_tax_source_gives_no_amount_at_all()
    {
        var unpriced = SalesInvoiceCalculator.Calculate([Line(1, "1", "10", Tax()), Line(2, "1", null, Tax())], Usd, 2, Rate(30m));
        var untaxed = SalesInvoiceCalculator.Calculate([Line(1, "1", "10", Tax()), Line(2, "1", "5", null)], Usd, 2, Rate(30m));

        Assert.Equal(SalesInvoiceAmountsState.Incomplete, unpriced.State);
        Assert.Null(unpriced.Totals);
        Assert.Empty(unpriced.Lines);
        Assert.Equal(SalesInvoiceAmountsState.TaxUnavailable, untaxed.State);
        Assert.Null(untaxed.FunctionalTotals);
        Assert.Contains(SalesLineTaxReasons.SnapshotMissing, untaxed.TaxReasons);
    }

    [Fact]
    public void A_rate_of_another_currency_than_the_invoice_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SalesInvoiceCalculator.Calculate([Line(1, "1", "10", Tax())], Try, 2, Rate(30m)));
    }

    [Fact]
    public void The_draft_freezes_the_amounts_and_splits_the_payable_into_its_instalments()
    {
        var terms = SalesDueTerms.Create(
        [
            new SalesDueInstalment(1, SalesDueBasis.ShipmentDate, 30, "50"),
            new SalesDueInstalment(2, SalesDueBasis.ShipmentDate, 60, "50"),
        ]);
        var due = SalesDueDateCalculator.Compute(terms, Day, Day);
        var invoice = SalesInvoice.Create(Guid.NewGuid(), Company, "SF-2026-000001", Header(Rate: null),
            [Share(1, "3", "33.33"), Share(2, "1", "0.02")], due.DueDates, terms, "actor-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(SalesInvoiceState.Draft, invoice.State);
        Assert.Equal(1, invoice.Version);
        Assert.Equal("120.01", Text(invoice.Totals.Payable));
        Assert.Equal(["60.01", "60.00"], invoice.Instalments.Select(item => Text(item.Amount)));
        Assert.Equal([Day.AddDays(30), Day.AddDays(60)], invoice.Instalments.Select(item => item.DueDate));
    }

    private static SalesInvoice MatchedForeignCurrencyInvoice(decimal delta)
    {
        var invoice = ApprovedForeignCurrencyInvoice();
        invoice.RecordMatch(invoice.Version, "matcher", Official(invoice, invoice.Totals.Payable.Amount, invoice.PayableFunctionalAmount + delta),
            0.05m, false, null, DateTimeOffset.UnixEpoch);
        return invoice;
    }

    private static SalesOfficialInvoiceFacts Official(SalesInvoice invoice, decimal payable, decimal payableFunctional) =>
        new("GIB2026000000001", new string('A', SalesInvoice.EttnLength), invoice.Header.PartyId, Usd, payable, payableFunctional);

    private static SalesInvoice ApprovedForeignCurrencyInvoice()
    {
        var terms = SalesDueTerms.Create(
        [
            new SalesDueInstalment(1, SalesDueBasis.ShipmentDate, 30, "50"),
            new SalesDueInstalment(2, SalesDueBasis.ShipmentDate, 60, "50"),
        ]);
        var due = SalesDueDateCalculator.Compute(terms, Day, Day);
        var header = Header(Rate: new SalesInvoiceRateSnapshot(34.5678m, Day, Day, Try, 2)) with { CurrencyId = Usd };
        var invoice = SalesInvoice.Create(Guid.NewGuid(), Company, "SF-2026-000002", header, [Share(1, "3", "33.33"), Share(2, "1", "0.02")],
            due.DueDates, terms, "preparer", DateTimeOffset.UnixEpoch);
        invoice.Submit(invoice.Version, "preparer", DateTimeOffset.UnixEpoch);
        invoice.Approve(invoice.Version, "approver", DateTimeOffset.UnixEpoch);
        return invoice;
    }

    [Theory]
    [InlineData("0.05")]
    [InlineData("-0.05")]
    public void A_foreign_currency_tl_difference_of_exactly_five_kurus_is_matched_without_approval_under_sat_d232(string delta)
    {
        // SAT-D232: the FX tolerance is 0,05 TL, inclusive, measured on the TL equivalent at the invoice's own locked rate.
        var tolerance = new GoymenERP.Modules.Sales.Application.Contracts.SalesInvoiceMatchTolerancePolicy().ToleranceAmount;
        Assert.Equal(0.05m, tolerance);
        var invoice = ApprovedForeignCurrencyInvoice();
        var match = invoice.RecordMatch(invoice.Version, "matcher",
            Official(invoice, invoice.Totals.Payable.Amount, invoice.PayableFunctionalAmount + decimal.Parse(delta, System.Globalization.CultureInfo.InvariantCulture)),
            tolerance, false, null, DateTimeOffset.UnixEpoch);

        Assert.Equal(0.05m, match.Difference);
        Assert.Equal(0.05m, match.Tolerance);
        Assert.False(match.DifferenceWasApproved);
        Assert.Equal(invoice.PayableFunctionalAmount, match.InvoiceFunctionalAmount);
    }

    [Fact]
    public void A_foreign_currency_tl_difference_of_six_kurus_needs_a_separate_approval_under_sat_d232()
    {
        var tolerance = new GoymenERP.Modules.Sales.Application.Contracts.SalesInvoiceMatchTolerancePolicy().ToleranceAmount;
        var invoice = ApprovedForeignCurrencyInvoice();
        var official = Official(invoice, invoice.Totals.Payable.Amount, invoice.PayableFunctionalAmount + 0.06m);

        var refused = Assert.Throws<SalesRuleException>(() => invoice.RecordMatch(invoice.Version, "matcher", official, tolerance, false,
            null, DateTimeOffset.UnixEpoch));
        Assert.Equal(SalesRuleCodes.InvoiceMatchDifferenceNotApproved, refused.Code);
        Assert.Null(invoice.Match);

        var match = invoice.RecordMatch(invoice.Version, "matcher", official, tolerance, true, "Kur yuvarlama farkı", DateTimeOffset.UnixEpoch);
        Assert.Equal(0.06m, match.Difference);
        Assert.Equal("matcher", match.DifferenceApprovedBy);
    }

    [Fact]
    public void The_foreign_currency_match_is_measured_on_tl_and_not_on_the_invoice_currency_amount()
    {
        // SAT-D232: there is no separate invoice-currency tolerance. A one-dollar gap with an equal TL equivalent is no difference,
        // and a TL gap above 0,05 is one even when the dollar amounts agree exactly.
        var tolerance = new GoymenERP.Modules.Sales.Application.Contracts.SalesInvoiceMatchTolerancePolicy().ToleranceAmount;
        var invoice = ApprovedForeignCurrencyInvoice();
        var dollarGap = invoice.EvaluateMatch(Official(invoice, invoice.Totals.Payable.Amount + 1m, invoice.PayableFunctionalAmount));
        Assert.Equal(0m, dollarGap.Difference);

        var tlGap = invoice.EvaluateMatch(Official(invoice, invoice.Totals.Payable.Amount, invoice.PayableFunctionalAmount - 0.06m));
        Assert.Equal(0.06m, tlGap.Difference);
        Assert.True(tlGap.Difference > tolerance);
        Assert.NotNull(invoice.FunctionalTotals);
        Assert.NotEqual(invoice.Totals.Payable.Amount, invoice.PayableFunctionalAmount);
    }

    [Fact]
    public void A_foreign_currency_invoice_finalized_with_an_exact_match_is_readable_again_from_the_store()
    {
        // Finalize and restore split and check the TL instalments against the same total: the ERP payable (SAT-D225/D227).
        var invoice = MatchedForeignCurrencyInvoice(0m);
        var finalization = invoice.Finalize(invoice.Version, "finalizer", Guid.NewGuid, DateTimeOffset.UnixEpoch);

        Assert.Equal(invoice.PayableFunctionalAmount, finalization.Plan.Sum(item => item.FunctionalAmount!.Value));

        var restored = SalesInvoice.Restore(invoice.Id, Company, invoice.DocumentNo, invoice.Header, invoice.State, invoice.Version,
            invoice.CreatedBy, invoice.CreatedAtUtc, invoice.Lines, invoice.Instalments, invoice.Decision);
        restored.RestoreMatch(invoice.Match!);
        restored.RestoreFinalization(finalization);
        Assert.Equal(finalization, restored.Finalization);
    }

    [Theory]
    [InlineData("0.03")]
    [InlineData("-0.03")]
    [InlineData("0.05")]
    public void A_match_with_a_difference_is_never_finalized_in_any_currency(string delta)
    {
        // NEEDS-SINAN #3: whether the official or the ERP amount governs is undecided, so a recorded difference fails closed.
        var invoice = MatchedForeignCurrencyInvoice(decimal.Parse(delta, System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotEqual(0m, invoice.Match!.Difference);

        var refused = Assert.Throws<SalesRuleException>(() => invoice.Finalize(invoice.Version, "finalizer", Guid.NewGuid,
            DateTimeOffset.UnixEpoch));

        Assert.Equal(SalesRuleCodes.InvoiceMatchDifferenceOpen, refused.Code);
        Assert.Equal(SalesInvoiceState.Approved, invoice.State);
        Assert.Null(invoice.Finalization);
    }

    [Fact]
    public void The_draft_refuses_one_shipment_twice_a_pending_due_date_and_an_unpriced_share()
    {
        var terms = TestDueTerms.PointDue30;
        var due = SalesDueDateCalculator.Compute(terms, Day, Day).DueDates;
        var twice = Share(1, "1", "10") with { };
        var duplicate = Assert.Throws<SalesRuleException>(() => SalesInvoice.Create(Guid.NewGuid(), Company, "SF-1", Header(null),
            [twice, Share(2, "1", "10") with { ShipmentId = twice.ShipmentId }], due, terms, "actor-1", DateTimeOffset.UnixEpoch));
        Assert.Equal(SalesRuleCodes.InvoiceShareDuplicated, duplicate.Code);

        var pending = SalesDueDateCalculator.Compute(
            SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.ShipmentDate, 30, "100")]), Day, null).DueDates;
        var missing = Assert.Throws<SalesRuleException>(() => SalesInvoice.Create(Guid.NewGuid(), Company, "SF-1", Header(null),
            [Share(1, "1", "10")], pending, SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.ShipmentDate, 30, "100")]),
            "actor-1", DateTimeOffset.UnixEpoch));
        Assert.Equal(SalesRuleCodes.InvoiceDueDateMissing, missing.Code);

        var unpriced = Assert.Throws<SalesRuleException>(() => SalesInvoice.Create(Guid.NewGuid(), Company, "SF-1", Header(null),
            [Share(1, "1", null)], due, terms, "actor-1", DateTimeOffset.UnixEpoch));
        Assert.Equal(SalesRuleCodes.InvoiceLineUnpriced, unpriced.Code);
    }

    [Fact]
    public void A_stored_line_whose_amounts_do_not_add_up_is_refused_on_restore()
    {
        var terms = TestDueTerms.PointDue30;
        var invoice = SalesInvoice.Create(Guid.NewGuid(), Company, "SF-1", Header(null), [Share(1, "1", "10")],
            SalesDueDateCalculator.Compute(terms, Day, Day).DueDates, terms, "actor-1", DateTimeOffset.UnixEpoch);
        var line = invoice.Lines[0];
        var restored = SalesInvoice.Restore(invoice.Id, Company, invoice.DocumentNo, invoice.Header, invoice.State, invoice.Version,
            invoice.CreatedBy, invoice.CreatedAtUtc, invoice.Lines, invoice.Instalments);
        Assert.Equal(invoice.Totals, restored.Totals);

        var corrupt = SalesInvoice.StoredLine(1, line.Share, line.NetUnitPrice, line.VatPercent, null,
            line.Amounts with { Payable = Money.Of(1m, Try, 2) }, null);
        Assert.Throws<InvalidOperationException>(() => SalesInvoice.Restore(invoice.Id, Company, invoice.DocumentNo, invoice.Header,
            invoice.State, invoice.Version, invoice.CreatedBy, invoice.CreatedAtUtc, [corrupt], invoice.Instalments));
    }

    [Fact]
    public void The_invoice_tables_are_company_filtered_and_one_shipment_is_on_one_active_line()
    {
        var company = new CompanyContext();
        company.Set(Company);
        using var context = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        foreach (var type in new[] { typeof(InvoiceRow), typeof(InvoiceLineRow), typeof(InvoiceAttemptRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(SalesDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
        }

        var lines = context.Model.FindEntityType(typeof(InvoiceLineRow))!;
        var active = lines.GetIndexes().Single(index => index.GetDatabaseName() == "UX_InvoiceLines_ActiveShipment");
        Assert.True(active.IsUnique);
        Assert.Equal(["CompanyId", "ShipmentId"], active.Properties.Select(property => property.Name));
        Assert.Equal(SalesDbContext.ActiveInvoiceShipmentFilter, active.GetFilter());
        Assert.Equal(SalesDbContext.InvoiceRateConstraint,
            context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(InvoiceRow))!.GetCheckConstraints().Single(check => check.Name == "CK_Invoices_Rate").Sql);

        var down = new AddSalesInvoices().DownOperations;
        var guard = Assert.IsType<SqlOperation>(down[0]);
        Assert.Contains("THROW 51221,", guard.Sql, StringComparison.Ordinal);
        Assert.Single(down.OfType<SqlOperation>());
        Assert.Empty(new AddSalesInvoices().UpOperations.OfType<SqlOperation>());
    }

    private static SalesInvoiceHeader Header(SalesInvoiceRateSnapshot? Rate) =>
        new(Guid.Parse("33333333-3333-4333-8333-333333333333"), Guid.Parse("44444444-4444-4444-8444-444444444444"), Try,
            Guid.Parse("88888888-8888-4888-8888-888888888888"), "1", 2, Day, Rate);

    private static SalesInvoiceShareInput Share(int lineNo, string quantity, string? price) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "IRS-2026-000001", lineNo, Day, Guid.NewGuid(), Guid.NewGuid(), 0, Product, Kg,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), quantity,
            new CommercialDraftFields(price, price is null ? null : Kg, null), Tax(vat: 20m));
}
