using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid InvoiceRateFunctionalCurrency = Guid.Parse("a2a10000-0000-4000-8000-0000000000c1");

    [SqlServerFact]
    public async Task Invoice_cost_rate_uses_the_finalized_official_source_before_separate_Finance_ack_without_writes()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run);
        var invoice = await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: true);
        var before = await InvoiceRateSourceCountsAsync(run);
        var answer = await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, receipt.AcceptanceId);
        Assert.Equal(PurchaseInvoiceCostRateOutcomeV1.Rate, answer.Outcome);
        Assert.Equal(35m, answer.Rate!.Value.Rate);
        Assert.Equal(InvoiceRateFunctionalCurrency, answer.Rate.Value.TargetCurrencyId);
        Assert.Equal(before, await InvoiceRateSourceCountsAsync(run));
        Assert.Equal(PurchaseInvoiceCostRateOutcomeV1.NotFound,
            (await ReadInvoiceCostRateAsync(run, CompanyB, CompanyB, receipt.AcceptanceId)).Outcome);
        await using var scope = run.Store(CompanyA);
        var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
        var current = (await store.FindIncomingInvoiceAsync(invoice.Id))!;
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinancePending, current.Status);
        await store.MutateIncomingInvoiceAsync(invoice.Id, PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance,
            run.Envelope("rate-ack"), Hash("rate-ack"), [],
            (value, _) => { value.AcknowledgeFinance(current.Version, current.AdmissionId!.Value, AcceptanceTime.AddMinutes(1)); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        Assert.Equal(answer, await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, receipt.AcceptanceId));
    }

    [SqlServerFact]
    public async Task Invoice_cost_rate_does_not_promote_a_matched_but_unfinalized_supplier_invoice()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run);
        await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: false);
        var answer = await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, receipt.AcceptanceId);
        Assert.Null(answer.Rate);
        Assert.Equal(PurchaseInvoiceCostRateReasonsV1.InvoiceNotFinalized, answer.UnavailableReason);
    }

    [SqlServerFact]
    public async Task Invoice_cost_rate_requires_full_coverage_and_accepts_two_finalized_sources_only_when_their_rates_agree()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run);
        await StoreRateInvoiceAsync(run, receipt, 1m, 35m, finalize: true);
        var partial = await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, receipt.AcceptanceId);
        Assert.Null(partial.Rate);
        Assert.Equal(PurchaseInvoiceCostRateReasonsV1.InvoiceCoverageIncomplete, partial.UnavailableReason);
        await StoreRateInvoiceAsync(run, receipt, 3m, 35m, finalize: true);
        var complete = await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, receipt.AcceptanceId);
        Assert.Equal(PurchaseInvoiceCostRateOutcomeV1.Rate, complete.Outcome);
        Assert.Equal(35m, complete.Rate!.Value.Rate);
    }

    [SqlServerFact]
    public async Task Invoice_cost_rate_never_chooses_or_averages_conflicting_official_rates_for_one_receipt()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run);
        await StoreRateInvoiceAsync(run, receipt, 1m, 35m, finalize: true);
        await StoreRateInvoiceAsync(run, receipt, 3m, 36m, finalize: true);
        var answer = await ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, receipt.AcceptanceId);
        Assert.Null(answer.Rate);
        Assert.Equal(PurchaseInvoiceCostRateReasonsV1.MultipleOfficialRates, answer.UnavailableReason);
    }

    [SqlServerFact]
    public async Task Invoice_cost_rate_refuses_a_cross_company_snapshot_even_when_the_row_filter_matches()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run);
        var invoice = await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: true);
        // Deliberate persisted corruption, not a production mutation path.
        await run.ExecuteAsync("UPDATE purchasing.IncomingInvoiceRevisions SET SnapshotJson=JSON_MODIFY(SnapshotJson,'$.companyId',@other) WHERE InvoiceId=@id AND Status=3",
            ("@other", CompanyB.ToString("D")), ("@id", invoice.Id));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ReadInvoiceCostRateAsync(run, CompanyA, CompanyA, receipt.AcceptanceId));
    }

    private static async Task<PurchaseInvoiceReceiptMatch> InvoiceRateReceiptAsync(Run run)
    {
        var (order, line, version) = await InvoiceCostRateOrderAsync(run,
            PurchaseOrderMoney.Of(InvoiceCostRateCurrency, "USD", InvoiceCostRateProfile, 1, 2), Day);
        var acceptance = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(new AcceptanceSeed(order, line, AcceptanceUnit, version), acceptance, "4"),
            Envelope("SENTETIK-rate-receipt-" + acceptance.ToString("N"), "SENTETIK-rate-receipt"));
        var current = await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", order));
        return new(CompanyA, Guid.Empty, acceptance, order, line, current, AcceptanceSupplier, AcceptanceAccount,
            InvoiceCostRateCurrency, AcceptanceUnit, 4m, 40m, 35m, Day);
    }

    private static async Task<PurchaseIncomingInvoice> StoreRateInvoiceAsync(Run run, PurchaseInvoiceReceiptMatch receipt,
        decimal quantity, decimal rate, bool finalize)
    {
        var id = Guid.NewGuid(); var lineId = Guid.NewGuid();
        var amounts = new PurchaseInvoiceAmounts(quantity * 10m, quantity * 2m, 0m);
        var functional = new PurchaseInvoiceAmounts(amounts.Net * rate, amounts.Vat * rate, 0m);
        var invoice = PurchaseIncomingInvoice.Record(id, CompanyA, AcceptanceSupplier, AcceptanceAccount,
            InvoiceCostRateCurrency, InvoiceRateFunctionalCurrency, 2, 2, rate,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-" + id.ToString("N"), Guid.NewGuid(), Day), amounts, functional,
            [new(lineId, 1, AcceptanceProduct, AcceptanceUnit, quantity, "material", "resale", 20m, amounts, functional)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, Day.AddDays(30), amounts.Payable, functional.Payable)], run.Actor, AcceptanceTime);
        await using var scope = run.Store(CompanyA);
        var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
        var key = "rate-record-" + id.ToString("N");
        await store.RecordIncomingInvoiceAsync(invoice, run.Envelope(key), Hash(key), _ => Task.CompletedTask);
        key = "rate-match-" + id.ToString("N");
        await store.MutateIncomingInvoiceAsync(id, PurchaseIncomingInvoiceCommandsV1.Match, run.Envelope(key), Hash(key), [receipt.OrderId],
            (value, _) => { value.Match(1, [receipt with { InvoiceLineId = lineId, Quantity = quantity, ExpectedNet = amounts.Net, ControlRate = rate }],
                new(Guid.NewGuid(), 1, 0m, 0m), run.Actor); return Task.CompletedTask; }, _ => Task.CompletedTask);
        if (finalize)
        {
            key = "rate-finalize-" + id.ToString("N");
            await store.MutateIncomingInvoiceAsync(id, PurchaseIncomingInvoiceCommandsV1.Finalize, run.Envelope(key), Hash(key), [],
                (value, _) => { value.Finalize(2, Guid.NewGuid(), run.Actor, AcceptanceTime); return Task.CompletedTask; }, _ => Task.CompletedTask);
        }
        return invoice;
    }

    private static async Task<(int Invoices, int Revisions, int Attempts, int Audit)> InvoiceRateSourceCountsAsync(Run run) => (
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoices"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
}
