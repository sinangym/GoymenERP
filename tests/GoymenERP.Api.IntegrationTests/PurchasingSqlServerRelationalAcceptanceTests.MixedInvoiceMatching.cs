using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    // SQL source coverage over existing independently approved owners, not a single mixed-order approval contract.
    // Settings/authority and Finance admission are existing synthetic ports; no real Finance debt/SQL acceptance claim.
    [SqlServerFact]
    public async Task Mixed_invoice_matches_partial_goods_service_and_expense_once_and_finalizes_one_invoice_source()
    {
        await using var run = await Run.StartAsync(); var sources = await MixedInvoiceSourcesAsync(run);
        var invoice = MixedInvoice(run, sources, 2m, 2m, 60m); await StoreExpenseInvoiceAsync(run, invoice);
        var stock = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        await using var scope = run.Store(CompanyA); var policy = new InvoiceMatchSettingsFixture();
        var finance = new InvoiceWorkflowFinanceProbe(_ => Task.CompletedTask);
        var workflow = InvoiceWorkflow(run, scope, finance, InvoiceMatchSource(scope, policy));
        var request = MixedInvoiceSelection(invoice, sources);
        var metadata = new PurchasingCommandMetadata("SENTETIK-mixed-match", run.Correlation, null, AcceptanceTime);
        var first = await workflow.MatchAsync(invoice.Id, request, metadata);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, first.Invoice.Status); Assert.Empty(first.Invoice.Restore().Variances);
        Assert.Equal(3, first.Invoice.Lines.Length); Assert.Equal(2, first.Invoice.Matches.Length);
        var goods = Assert.Single(first.Invoice.Matches, x => x.ServiceTypeId is null);
        var service = Assert.Single(first.Invoice.Matches, x => x.ServiceTypeId is not null);
        Assert.Equal((sources.Goods.OrderId, sources.Goods.AcceptanceId, 2m, 20m),
            (goods.OrderId, goods.AcceptanceId, goods.Quantity, goods.ExpectedNet));
        Assert.Equal(4m, goods.AcceptedQuantity);
        Assert.Equal((sources.Service.OrderId, sources.ServiceAcceptance, sources.Service.Lines[0].ServiceTypeId, 2m),
            (service.OrderId, service.AcceptanceId, service.ServiceTypeId!.Value, service.Quantity));
        Assert.Null(service.AcceptedQuantity); // Existing service match wire does not populate this optional goods field.
        Assert.Equal(4m, await run.ScalarAsync<decimal>("SELECT Quantity FROM purchasing.ServiceAcceptances WHERE AcceptanceId=@id", ("@id", sources.ServiceAcceptance)));
        Assert.Equal(4m, service.ExpectedNet);
        Assert.Equal((sources.Cost.CostId, 60m), (Assert.Single(first.Invoice.AdditionalCosts!).CostId, first.Invoice.AdditionalCosts![0].NetAmount));
        policy.Available = false;
        var replay = await workflow.MatchAsync(invoice.Id, request, metadata with { CorrelationId = "SENTETIK-mixed-retry" });
        Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(first.Invoice.Version, replay.Invoice.Version); Assert.Equal(first.Invoice.Policy, replay.Invoice.Policy);
        Assert.Equal(JsonSerializer.Serialize(first.Invoice.Matches), JsonSerializer.Serialize(replay.Invoice.Matches));
        Assert.Equal(JsonSerializer.Serialize(first.Invoice.AdditionalCosts), JsonSerializer.Serialize(replay.Invoice.AdditionalCosts));
        Assert.Equal(1, policy.PolicyReads);
        Assert.Equal((2m, 2m, 60m), await MixedConsumedAsync(run, sources));
        await AssertMixedBindingCountsAsync(run, invoice.Id, 1);
        Assert.Equal(stock, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));

        var finalize = metadata with { IdempotencyKey = "SENTETIK-mixed-finalize" };
        var finalized = await workflow.FinalizeAsync(invoice.Id, new("2"), finalize);
        var finalizedAgain = await workflow.FinalizeAsync(invoice.Id, new("2"), finalize);
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinanceAccepted, finalized.Invoice.Status);
        Assert.True(finalizedAgain.IsReplay); Assert.Equal(finalized.Invoice.AdmissionId, finalizedAgain.Invoice.AdmissionId); Assert.Equal(1, finance.Calls);
        var payload = PurchaseIncomingInvoiceFinalizationV1.Event(finalized.Invoice.Restore());
        Assert.Equal(invoice.Id, payload.InvoiceId); Assert.Equal(3, payload.Lines.Count); Assert.Equal(invoice.Amounts.Payable, payload.Payable);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages WHERE EventType=N'purchasing.invoice.finalized.v1' AND AggregateId=@id", ("@id", invoice.Id)));
        Assert.Equal(stock, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
    }

    [SqlServerFact]
    public async Task Mixed_invoice_overconsumption_of_any_owner_aborts_all_three_bindings_and_preserves_remaining_shares()
    {
        await using var run = await Run.StartAsync(); var sources = await MixedInvoiceSourcesAsync(run);
        var first = MixedInvoice(run, sources, 2m, 2m, 60m); await StoreExpenseInvoiceAsync(run, first);
        await MatchMixedInvoiceAsync(run, first, sources, "SENTETIK-mixed-first");
        foreach (var quantities in new[] { (Goods: 3m, Service: 1m, Expense: 10m), (Goods: 1m, Service: 3m, Expense: 10m), (Goods: 1m, Service: 1m, Expense: 50m) })
        {
            var excess = MixedInvoice(run, sources, quantities.Goods, quantities.Service, quantities.Expense); await StoreExpenseInvoiceAsync(run, excess);
            var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => MatchMixedInvoiceAsync(run, excess, sources, "SENTETIK-mixed-excess-" + excess.Id.ToString("N")));
            Assert.Equal((409, PurchaseIncomingInvoice.SourceMismatch), (refusal.Status, refusal.Code));
            await AssertMixedBindingCountsAsync(run, excess.Id, 0);
            Assert.Equal((2m, 2m, 60m), await MixedConsumedAsync(run, sources));
            await using var scope = run.Store(CompanyA);
            Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, (await ((IPurchasingIncomingInvoiceStoreV1)scope.Store).FindIncomingInvoiceAsync(excess.Id))!.Status);
        }
        var remaining = MixedInvoice(run, sources, 2m, 2m, 40m); await StoreExpenseInvoiceAsync(run, remaining);
        var matched = await MatchMixedInvoiceAsync(run, remaining, sources, "SENTETIK-mixed-remaining");
        Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, matched.Invoice.Status);
        Assert.Equal((4m, 4m, 100m), await MixedConsumedAsync(run, sources));
        await AssertMixedBindingCountsAsync(run, first.Id, 1); await AssertMixedBindingCountsAsync(run, remaining.Id, 1);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceAcceptances WHERE OrderId=@id", ("@id", sources.Service.OrderId)));
    }

    [SqlServerFact]
    public async Task Mixed_invoice_owner_sources_and_all_three_consumption_ledgers_are_company_isolated()
    {
        await using var run = await Run.StartAsync(); var sources = await MixedInvoiceSourcesAsync(run);
        var invoice = MixedInvoice(run, sources, 2m, 2m, 60m); await StoreExpenseInvoiceAsync(run, invoice);
        await MatchMixedInvoiceAsync(run, invoice, sources, "SENTETIK-mixed-company");
        await using var foreign = run.Store(CompanyB); var company = new CompanyContext(); company.Set(CompanyB);
        Assert.Null(await ((IPurchasingIncomingInvoiceStoreV1)foreign.Store).FindIncomingInvoiceAsync(invoice.Id));
        Assert.Null(await new PurchaseAcceptanceSource(foreign.Context, company).ReadAsync(sources.Goods.OrderId, sources.Goods.AcceptanceId));
        await using var foreignServices = ServiceScope(run, CompanyB);
        Assert.Null(await foreignServices.Store.ReadAsync(sources.Service.OrderId, default));
        Assert.Empty(await foreign.Context.InvoiceReceiptBindings.AsNoTracking().ToArrayAsync());
        Assert.Empty(await foreign.Context.ServiceInvoiceBindings.AsNoTracking().ToArrayAsync());
        Assert.Empty(await foreign.Context.AdditionalCostInvoiceBindings.AsNoTracking().ToArrayAsync());
        Assert.Empty(await foreign.Context.AdditionalCosts.AsNoTracking().ToArrayAsync());
        // Tenant filtering, not a workflow paired with a foreign context: the real owner workspace sees no company-A invoice.
        await using var owner = run.Store(CompanyA);
        var workflow = InvoiceWorkflow(run, owner, new(_ => Task.CompletedTask));
        var workspace = new PurchaseIncomingInvoiceWorkspace(foreign.Context, company,
            new NumberTestUser { UserId = run.Actor }, new NumberTestAuthority(), workflow);
        Assert.Empty((await workspace.ListAsync(invoice.Source.OfficialNumber, 0)).Items);
        await AssertMixedBindingCountsAsync(run, invoice.Id, 1); Assert.Equal((2m, 2m, 60m), await MixedConsumedAsync(run, sources));
    }

    private sealed record MixedInvoiceSources(PurchaseInvoiceReceiptMatch Goods, PurchaseServiceOrderState Service,
        Guid ServiceAcceptance, PurchaseAdditionalCostState Cost);

    private static async Task<MixedInvoiceSources> MixedInvoiceSourcesAsync(Run run)
    {
        var goods = await InvoiceRateReceiptAsync(run, new PurchaseOrderLinePrice(10m, AcceptanceUnit));
        PurchaseAdditionalCostState cost;
        await using (var scope = AdditionalCostScope(run, CompanyA))
        {
            var source = Assert.IsType<PurchaseAdditionalCostSourceV1>(await scope.Store.ReadSourceAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, goods.OrderId, null, default));
            cost = AdditionalCostFact(run, source) with { SupplierPartyId = goods.SupplierPartyId, CompanyAccountId = goods.CompanyAccountId,
                NetAmount = 100m, VatAmount = 20m, GrossAmount = 120m };
            await scope.Store.RecordAsync(cost.CostId, AdditionalCostEnvelope(run, "SENTETIK-mixed-expense-source"), Hash("SENTETIK-mixed-expense-source"),
                _ => Task.FromResult(cost), _ => Task.CompletedTask, default);
        }
        await using var service = ServiceScope(run, CompanyA);
        var draft = ServiceDraft(run) with { SupplierPartyId = goods.SupplierPartyId, CompanyAccountId = goods.CompanyAccountId, Money = cost.Money };
        var acceptance = Guid.NewGuid();
        await service.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("SENTETIK-mixed-service-create"), Hash("SENTETIK-mixed-service-create"), "SENTETIK hizmet",
            (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
        await service.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve, run.Envelope("SENTETIK-mixed-service-approve"), Hash("SENTETIK-mixed-service-approve"), "SENTETIK onay",
            (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1, "SENTETIK-checker", old!.CreatedAtUtc)), _ => Task.CompletedTask, default);
        var accepted = await service.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Accept, run.Envelope("SENTETIK-mixed-service-accept"), Hash("SENTETIK-mixed-service-accept"), "SENTETIK kabul",
            (old, _) => Task.FromResult(PurchaseServiceOrder.Accept(old!, 2, acceptance, draft.Lines[0].LineId, 4m, Day, run.Actor, "SENTETIK kısmi kabul", old!.CreatedAtUtc)), _ => Task.CompletedTask, default);
        var recorded = Assert.IsType<PurchaseServiceOrderState>(accepted?.Order);
        return new(goods, recorded, acceptance, cost);
    }

    private static PurchaseIncomingInvoice MixedInvoice(Run run, MixedInvoiceSources sources, decimal goodsQuantity, decimal serviceQuantity, decimal expenseNet)
    {
        var goods = new PurchaseInvoiceAmounts(goodsQuantity * 10m, goodsQuantity * 2m, 0m);
        var service = new PurchaseInvoiceAmounts(serviceQuantity * 2m, 0m, 0m);
        var expense = new PurchaseInvoiceAmounts(expenseNet, expenseNet * .2m, 0m);
        var amounts = new PurchaseInvoiceAmounts(goods.Net + service.Net + expense.Net, goods.Vat + expense.Vat, 0m);
        var id = Guid.NewGuid();
        return PurchaseIncomingInvoice.Record(id, CompanyA, sources.Goods.SupplierPartyId, sources.Goods.CompanyAccountId,
            sources.Goods.CurrencyId, sources.Goods.CurrencyId, 2, 2, null,
            new(Guid.NewGuid(), 1, new string('d', 64), "SENTETIK-MIXED-" + id.ToString("N"), Guid.NewGuid(), Day), amounts, null,
            [new(Guid.NewGuid(), 1, AcceptanceProduct, AcceptanceUnit, goodsQuantity, "material", "resale", 20m, goods, null),
             new(Guid.NewGuid(), 2, Guid.Empty, sources.Service.Lines[0].UnitId, serviceQuantity, "Service", "service", 0m, service, null, sources.Service.Lines[0].ServiceTypeId),
             new(Guid.NewGuid(), 3, Guid.Empty, Guid.NewGuid(), 1m, "Expense", "expense", 20m, expense, null, null, sources.Cost.ExpenseType?.Id)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, Day.AddDays(30), amounts.Payable, null)], run.Actor, AcceptanceTime);
    }

    private static PurchaseIncomingInvoiceMatchRequestV1 MixedInvoiceSelection(PurchaseIncomingInvoice invoice, MixedInvoiceSources sources) => new("1",
        [new(invoice.Lines[0].Id, sources.Goods.OrderId, sources.Goods.AcceptanceId, MixedText(invoice.Lines[0].Quantity)),
         new(invoice.Lines[1].Id, sources.Service.OrderId, sources.ServiceAcceptance, MixedText(invoice.Lines[1].Quantity))],
        [new(invoice.Lines[2].Id, sources.Cost.OrderId, sources.Cost.CostId, MixedText(invoice.Lines[2].Amounts.Net))]);
    private static string MixedText(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static async Task<PurchaseIncomingInvoiceOperationResultV1> MatchMixedInvoiceAsync(Run run, PurchaseIncomingInvoice invoice, MixedInvoiceSources sources, string key)
    {
        await using var scope = run.Store(CompanyA);
        return await InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, new())).MatchAsync(invoice.Id,
            MixedInvoiceSelection(invoice, sources), new(key, run.Correlation, null, AcceptanceTime));
    }
    private static async Task<(decimal Goods, decimal Service, decimal Expense)> MixedConsumedAsync(Run run, MixedInvoiceSources sources) => (
        await run.ScalarAsync<decimal>("SELECT COALESCE(SUM(Quantity),0) FROM purchasing.InvoiceReceiptBindings WHERE AcceptanceId=@id", ("@id", sources.Goods.AcceptanceId)),
        await run.ScalarAsync<decimal>("SELECT COALESCE(SUM(Quantity),0) FROM purchasing.ServiceInvoiceBindings WHERE AcceptanceId=@id", ("@id", sources.ServiceAcceptance)),
        await run.ScalarAsync<decimal>("SELECT COALESCE(SUM(NetAmount),0) FROM purchasing.AdditionalCostInvoiceBindings WHERE CostId=@id", ("@id", sources.Cost.CostId)));
    private static async Task AssertMixedBindingCountsAsync(Run run, Guid invoice, int expected)
    {
        Assert.Equal(expected, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceReceiptBindings WHERE InvoiceId=@id", ("@id", invoice)));
        Assert.Equal(expected, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceInvoiceBindings WHERE InvoiceId=@id", ("@id", invoice)));
        Assert.Equal(expected, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCostInvoiceBindings WHERE InvoiceId=@id", ("@id", invoice)));
    }
}
