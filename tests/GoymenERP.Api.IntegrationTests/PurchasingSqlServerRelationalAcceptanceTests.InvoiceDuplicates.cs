using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>WP15 (1.16): the duplicate facts are stored with the record, read back as candidates, and legacy rows fall back.</summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Recorded_invoices_store_duplicate_facts_and_the_candidate_read_finds_number_and_date_amount_matches_including_legacy_rows()
    {
        await using var run = await Run.StartAsync(); var cost = await InvoiceExpenseFactAsync(run);
        var first = DuplicateInvoice(run, cost, "SENTETIK-DUP-0001", 1000m);
        var second = DuplicateInvoice(run, cost, "SENTETIK-DUP-0002", 2000m);
        await StoreExpenseInvoiceAsync(run, first); await StoreExpenseInvoiceAsync(run, second);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.IncomingInvoices WHERE InvoiceDate IS NOT NULL AND CurrencyId IS NOT NULL AND PayableAmount IS NOT NULL"));
        Assert.Equal(1200m, await run.ScalarAsync<decimal>(
            "SELECT PayableAmount FROM purchasing.IncomingInvoices WHERE InvoiceId = @id", ("@id", first.Id)));
        // A row written before the columns existed keeps nulls; its facts come from the first revision.
        await run.ExecuteAsync(
            "UPDATE purchasing.IncomingInvoices SET InvoiceDate = NULL, CurrencyId = NULL, PayableAmount = NULL WHERE InvoiceId = @id", ("@id", second.Id));

        await using (var scope = run.Store(CompanyA))
        {
            var source = (IPurchasingInvoiceDuplicateCandidateSourceV1)scope.Store;
            var byNumber = PurchaseInvoiceDuplicateRule.Evaluate(new(cost.SupplierPartyId, "sentetik dup 0001", Guid.NewGuid(), Day.AddDays(3),
                cost.Money.CurrencyId, 1m), await source.ListDuplicateCandidatesAsync(new(cost.SupplierPartyId, "sentetik dup 0001",
                Guid.NewGuid(), Day.AddDays(3), cost.Money.CurrencyId, 1m)));
            Assert.Equal([(first.Id, PurchaseInvoiceDuplicateReason.SameSupplierAndNumber)], byNumber.Select(m => (m.InvoiceId, m.Reason)));
            var legacyProbe = new PurchaseInvoiceDuplicateProbe(cost.SupplierPartyId, "SENTETIK-OTHER", Guid.NewGuid(), Day,
                cost.Money.CurrencyId, 2400m);
            var legacy = PurchaseInvoiceDuplicateRule.Evaluate(legacyProbe, await source.ListDuplicateCandidatesAsync(legacyProbe));
            Assert.Equal([(second.Id, PurchaseInvoiceDuplicateReason.SameSupplierDateAndAmount)], legacy.Select(m => (m.InvoiceId, m.Reason)));
            var ettnProbe = new PurchaseInvoiceDuplicateProbe(Guid.NewGuid(), "X", first.Source.Ettn, Day.AddDays(9), cost.Money.CurrencyId, 5m);
            Assert.Equal([first.Id], PurchaseInvoiceDuplicateRule.Evaluate(ettnProbe, await source.ListDuplicateCandidatesAsync(ettnProbe))
                .Select(m => m.InvoiceId));
        }

        await using var foreign = run.Store(CompanyB);
        Assert.Empty(await ((IPurchasingInvoiceDuplicateCandidateSourceV1)foreign.Store).ListDuplicateCandidatesAsync(
            new(cost.SupplierPartyId, "SENTETIK-DUP-0001", first.Source.Ettn, Day, cost.Money.CurrencyId, 1200m)));
    }

    private static PurchaseIncomingInvoice DuplicateInvoice(Run run, PurchaseAdditionalCostState cost, string number, decimal net)
    {
        var id = Guid.NewGuid(); var amounts = new PurchaseInvoiceAmounts(net, net * .2m, 0m);
        return PurchaseIncomingInvoice.Record(id, CompanyA, cost.SupplierPartyId, cost.CompanyAccountId, cost.Money.CurrencyId, cost.Money.CurrencyId, 2, 2, null,
            new(Guid.NewGuid(), 1, new string('a', 64), number, Guid.NewGuid(), Day), amounts, null,
            [new(Guid.NewGuid(), 1, Guid.Empty, Guid.NewGuid(), 1m, "Expense", "expense", 20m, amounts, null)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, Day.AddDays(1), amounts.Payable, null)], run.Actor, DateTimeOffset.UtcNow);
    }
}
