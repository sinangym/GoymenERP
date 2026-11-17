using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Expense_invoice_matching_uses_real_partial_money_shares_for_two_invoices_and_preserves_stock_and_legacy_receipt_ledgers()
    {
        await using var run = await Run.StartAsync(); var cost = await InvoiceExpenseFactAsync(run);
        var stock = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        var first = ExpenseInvoice(run, cost, 6000m); var second = ExpenseInvoice(run, cost, 4000m);
        await StoreExpenseInvoiceAsync(run, first); await StoreExpenseInvoiceAsync(run, second);
        var one = await MatchExpenseInvoiceAsync(run, first, cost, "SENTETIK-expense-first");
        var two = await MatchExpenseInvoiceAsync(run, second, cost, "SENTETIK-expense-second");
        Assert.Equal(6000m, Assert.Single(one.Invoice.AdditionalCosts!).NetAmount); Assert.Empty(one.Invoice.Matches);
        Assert.Equal(4000m, Assert.Single(two.Invoice.AdditionalCosts!).NetAmount); Assert.Empty(two.Invoice.Matches);
        Assert.True((await MatchExpenseInvoiceAsync(run, first, cost, "SENTETIK-expense-first")).IsReplay);
        Assert.Equal(10000m, await run.ScalarAsync<decimal>("SELECT SUM(NetAmount) FROM purchasing.AdditionalCostInvoiceBindings"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceReceiptBindings"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseInvoiceOrderBindings"));
        Assert.Equal(stock, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        var excess = ExpenseInvoice(run, cost, 1m); await StoreExpenseInvoiceAsync(run, excess);
        Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MatchExpenseInvoiceAsync(run, excess, cost, "SENTETIK-expense-exhausted"))).Status);
        await using var foreign = run.Store(CompanyB);
        Assert.Empty(await foreign.Context.Set<PurchaseAdditionalCostInvoiceBindingRow>().AsNoTracking().ToArrayAsync());
    }

    [SqlServerFact]
    public async Task Concurrent_expense_invoice_matches_cannot_spend_the_same_immutable_expense_budget_twice()
    {
        await using var run = await Run.StartAsync(); var cost = await InvoiceExpenseFactAsync(run);
        var first = ExpenseInvoice(run, cost, 6000m); var second = ExpenseInvoice(run, cost, 6000m);
        await StoreExpenseInvoiceAsync(run, first); await StoreExpenseInvoiceAsync(run, second);
        async Task<bool> Try(PurchaseIncomingInvoice invoice, string key)
        {
            try { await MatchExpenseInvoiceAsync(run, invoice, cost, key); return true; }
            catch (PurchasingServiceException exception) when (exception.Status == 409) { return false; }
        }
        var answers = await Task.WhenAll(Try(first, "SENTETIK-expense-race-a"), Try(second, "SENTETIK-expense-race-b"));
        Assert.Single(answers, x => x);
        Assert.Equal(6000m, await run.ScalarAsync<decimal>("SELECT SUM(NetAmount) FROM purchasing.AdditionalCostInvoiceBindings"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCostInvoiceBindings"));
    }

    [SqlServerFact]
    public async Task Expense_invoice_late_permission_loss_rolls_back_the_budget_ledger_and_Down_51939_preserves_a_committed_binding()
    {
        await using var run = await Run.StartAsync(); var cost = await InvoiceExpenseFactAsync(run);
        var invoice = ExpenseInvoice(run, cost, 6000m); await StoreExpenseInvoiceAsync(run, invoice);
        await using (var scope = run.Store(CompanyA))
        {
            var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.MutateIncomingInvoiceAsync(invoice.Id,
                PurchaseIncomingInvoiceCommandsV1.Match, run.Envelope("SENTETIK-expense-last-fence"), Hash("SENTETIK-expense-last-fence"), [cost.OrderId],
                (current, _) => { current.Match(1, [], new(Guid.NewGuid(), 1, 0, 0), run.Actor, [ExpenseMatch(invoice, cost)]); return Task.CompletedTask; },
                async token => { if (await scope.Context.Set<PurchaseAdditionalCostInvoiceBindingRow>().AsNoTracking().AnyAsync(token))
                    throw new UnauthorizedAccessException("SENTETIK yetki kaybı"); }));
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCostInvoiceBindings"));
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseAdditionalCostInvoiceBindings().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await MatchExpenseInvoiceAsync(run, invoice, cost, "SENTETIK-expense-down");
        Assert.Equal(51939, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCostInvoiceBindings"));
    }

    // Purchasing source/transaction proof. Settings tolerance and authority remain explicit synthetic fixtures;
    // actual expense/order rows, partial budget ledger, Audit, concurrency and tenant filtering are SQL-owned.
    private static async Task<PurchaseAdditionalCostState> InvoiceExpenseFactAsync(Run run)
    {
        var receipt = await InvoiceRateReceiptAsync(run); await using var scope = AdditionalCostScope(run, CompanyA);
        var source = await scope.Store.ReadSourceAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, receipt.OrderId, null, default);
        var cost = AdditionalCostFact(run, source!) with { NetAmount = 10000m, VatAmount = 2000m, GrossAmount = 12000m };
        await scope.Store.RecordAsync(cost.CostId, AdditionalCostEnvelope(run, "SENTETIK-expense-invoice-source"), Hash("SENTETIK-expense-invoice-source"),
            _ => Task.FromResult(cost), _ => Task.CompletedTask, default);
        return cost;
    }
    private static PurchaseIncomingInvoice ExpenseInvoice(Run run, PurchaseAdditionalCostState cost, decimal net)
    {
        var id = Guid.NewGuid(); var amounts = new PurchaseInvoiceAmounts(net, net * .2m, 0m);
        return PurchaseIncomingInvoice.Record(id, CompanyA, cost.SupplierPartyId, cost.CompanyAccountId, cost.Money.CurrencyId, cost.Money.CurrencyId, 2, 2, null,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-EXP-" + id.ToString("N"), Guid.NewGuid(), Day), amounts, null,
            [new(Guid.NewGuid(), 1, Guid.Empty, Guid.NewGuid(), 1m, "Expense", "expense", 20m, amounts, null)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, Day.AddDays(1), amounts.Payable, null)], run.Actor, DateTimeOffset.UtcNow);
    }
    private static PurchaseInvoiceAdditionalCostMatch ExpenseMatch(PurchaseIncomingInvoice invoice, PurchaseAdditionalCostState cost) =>
        new(CompanyA, invoice.Lines[0].Id, cost.CostId, cost.OrderId, cost.SourceOrderVersion, cost.SupplierPartyId, cost.CompanyAccountId,
            cost.Money.CurrencyId, invoice.Amounts.Net, cost.NetAmount, cost.Tax.VatPercent, cost.BusinessDate, cost.ExpenseType?.Id);
    private static async Task StoreExpenseInvoiceAsync(Run run, PurchaseIncomingInvoice invoice)
    {
        await using var scope = run.Store(CompanyA); var key = "SENTETIK-expense-record-" + invoice.Id.ToString("N");
        await ((IPurchasingIncomingInvoiceStoreV1)scope.Store).RecordIncomingInvoiceAsync(invoice, run.Envelope(key), Hash(key), _ => Task.CompletedTask);
    }
    private static async Task<PurchaseIncomingInvoiceOperationResultV1> MatchExpenseInvoiceAsync(Run run, PurchaseIncomingInvoice invoice,
        PurchaseAdditionalCostState cost, string key)
    {
        await using var scope = run.Store(CompanyA);
        var workflow = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, new()));
        return await workflow.MatchAsync(invoice.Id, new("1", [], [new(invoice.Lines[0].Id, cost.OrderId, cost.CostId,
            invoice.Amounts.Net.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture))]),
            new(key, run.Correlation, null, DateTimeOffset.UtcNow));
    }
}
