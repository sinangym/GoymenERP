using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesSubcontractServiceInvoiceTests
{
    private static readonly Guid Foreign = Guid.Parse("fbfbfbfb-fbfb-4bfb-8bfb-fbfbfbfbfbfb");

    private static SalesSubcontractServiceInvoice LifecycleInvoice(bool foreign = false)
    {
        var invoice = SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), Company, "SFH-2026-000002",
            Header() with { CurrencyId = foreign ? Foreign : Try }, Source("10"), [Line("10", "2.50")],
            10m, "SENTETIK-preparer", Now);
        var terms = SalesDueTerms.Create([new(1, SalesDueBasis.OrderDate, 30, "100")]);
        invoice.SetDuePlan(terms, SalesDueDateCalculator.Compute(terms, Day, shipmentDate: null).DueDates);
        return invoice;
    }

    private static SalesOfficialInvoiceFacts Official(SalesSubcontractServiceInvoice invoice, DateOnly date, decimal factor = 1m) =>
        new("SENTETIK-SFH-1", "f0f0f0f0-f0f0-40f0-80f0-f0f0f0f0f0f0", Party, invoice.Header.CurrencyId,
            invoice.Totals.Payable.Amount, invoice.Totals.Payable.Amount * factor, invoice.Totals.Vat.Amount * factor,
            invoice.Totals.Withholding.Amount * factor, date,
            new(Company, Guid.NewGuid(), "approved-import", "2", Now, null, null, null, Guid.NewGuid(), new string('a', 64), false));

    private static void ApprovedService(SalesSubcontractServiceInvoice invoice)
    {
        invoice.Submit(1, "SENTETIK-submitter", Now);
        invoice.Approve(2, "SENTETIK-checker", Now);
    }

    [Fact]
    public void Subcontract_foreign_invoice_uses_the_actual_official_day_rate_and_preserves_native_amounts_and_due_plan()
    {
        var invoice = LifecycleInvoice(foreign: true);
        ApprovedService(invoice);
        var original = invoice.Totals;
        var date = Day.AddDays(3);
        var official = Official(invoice, date, 40m);
        var rate = new SalesInvoiceRateSnapshot(40m, date, date.AddDays(-1), Try, 2);
        invoice.RecordMatch(3, "SENTETIK-matcher", official, Try, rate, 0.05m, false, null, Now);
        Assert.Equal(4, invoice.Version);
        Assert.Equal(date, invoice.Rate!.RateDay);
        Assert.NotEqual(invoice.Header.BusinessDate, invoice.Rate.RateDay);
        Assert.Equal(original, invoice.Totals);
        Assert.Equal(1200m, invoice.FunctionalTotals!.Payable.Amount);
        var final = invoice.Finalize(4, "SENTETIK-finalizer", Guid.NewGuid, Now);
        Assert.Equal(SalesInvoiceFinanceStatus.Waiting, final.FinanceStatus);
        Assert.Equal(Day.AddDays(30), Assert.Single(final.Plan).DueDate);
        Assert.Equal((30m, 1200m), (final.Plan[0].Amount, final.Plan[0].FunctionalAmount));
        var restored = SalesSubcontractServiceInvoice.Restore(invoice.Id, Company, invoice.DocumentNo, invoice.Header,
            invoice.Source, invoice.State, invoice.Version, invoice.CreatedBy, invoice.CreatedAtUtc, invoice.Lines, invoice.LifecycleFacts);
        Assert.Equal(invoice.Match, restored.Match);
        Assert.Equal(invoice.FunctionalTotals, restored.FunctionalTotals);
        Assert.Equal(final, restored.Finalization);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("draft-day")]
    [InlineData("foreign-company")]
    public void Subcontract_foreign_match_without_actual_official_date_rate_or_company_proof_records_nothing(string missing)
    {
        var invoice = LifecycleInvoice(foreign: true);
        ApprovedService(invoice);
        var date = Day.AddDays(3);
        var official = Official(invoice, date, 40m);
        if (missing == "foreign-company") official = official with { SourceProof = official.SourceProof! with { CompanyId = Guid.NewGuid() } };
        var rate = missing == "missing" ? null : new SalesInvoiceRateSnapshot(40m,
            missing == "draft-day" ? Day : date, date.AddDays(-1), Try, 2);
        Assert.Throws<SalesRuleException>(() => invoice.RecordMatch(3, "SENTETIK-matcher", official, Try, rate, 0.05m, false, null, Now));
        Assert.Null(invoice.Match);
        Assert.Null(invoice.Rate);
        Assert.Empty(invoice.FunctionalLines);
        Assert.Equal(3, invoice.Version);
    }

    [Fact]
    public void Subcontract_preparer_and_submitter_cannot_approve_and_legacy_draft_has_no_assumed_due_date()
    {
        var legacy = Create("10", Line("10", "2.50"));
        Assert.Equal(SalesRuleCodes.InvoiceDueDateMissing,
            Assert.Throws<SalesRuleException>(() => legacy.Submit(1, "SENTETIK-submitter", Now)).Code);
        var invoice = LifecycleInvoice();
        invoice.Submit(1, "SENTETIK-submitter", Now);
        foreach (var actor in new[] { "SENTETIK-preparer", "SENTETIK-submitter" })
            Assert.Equal(SalesRuleCodes.InvoiceSelfApproval, Assert.Throws<SalesRuleException>(() => invoice.Approve(2, actor, Now)).Code);
        Assert.Equal(SalesSubcontractServiceInvoiceState.PendingApproval, invoice.State);
    }

    [Fact]
    public void Subcontract_official_match_keeps_shared_difference_policy_and_finalization_waits_for_exact_resolution()
    {
        var invoice = LifecycleInvoice();
        ApprovedService(invoice);
        var official = Official(invoice, Day) with { PayableFunctionalAmount = 30.01m };
        invoice.RecordMatch(3, "SENTETIK-matcher", official, Try, null, 0.05m, false, null, Now);
        Assert.Equal(SalesRuleCodes.InvoiceMatchDifferenceOpen,
            Assert.Throws<SalesRuleException>(() => invoice.Finalize(4, "SENTETIK-finalizer", Guid.NewGuid, Now)).Code);
        Assert.Null(invoice.Finalization);
    }
}
