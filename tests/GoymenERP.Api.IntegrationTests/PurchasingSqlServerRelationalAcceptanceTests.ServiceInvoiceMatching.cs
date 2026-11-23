using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Service_invoice_matching_uses_actual_service_acceptance_and_never_creates_a_goods_or_Inventory_binding()
    {
        await using var run = await Run.StartAsync();
        var (order, acceptance) = await ServiceInvoiceAcceptanceAsync(run);
        var invoice = ServiceInvoice(run, order);
        await using var scope = run.Store(CompanyA);
        await ((IPurchasingIncomingInvoiceStoreV1)scope.Store).RecordIncomingInvoiceAsync(invoice,
            run.Envelope("service-invoice-record"), Hash("service-invoice-record"), _ => Task.CompletedTask);
        var policy = new InvoiceMatchSettingsFixture();
        var workflow = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, policy));
        var company = new CompanyContext(); company.Set(CompanyA);
        var workspace = new PurchaseIncomingInvoiceWorkspace(scope.Context, company,
            new NumberTestUser { UserId = run.Actor }, new NumberTestAuthority(), workflow);
        var candidate = Assert.Single((await workspace.CandidatesAsync(invoice.Id, invoice.Lines[0].Id, 0)).Items);
        Assert.Equal(order.OrderId, candidate.PurchaseOrderId);
        Assert.Equal(acceptance, candidate.AcceptanceId); Assert.Equal(Guid.Empty, candidate.ProductId);
        Assert.Equal(order.Lines[0].ServiceTypeId, candidate.ServiceTypeId);
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1", [new(invoice.Lines[0].Id, order.OrderId, acceptance, "4")]);
        var metadata = new PurchasingCommandMetadata("SENTETIK-service-invoice-match", run.Correlation, null, AcceptanceTime);
        var matched = await workflow.MatchAsync(invoice.Id, request, metadata);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, matched.Invoice.Status);
        Assert.Equal(8m, Assert.Single(matched.Invoice.Matches).ExpectedNet);
        Assert.Equal(order.Lines[0].ServiceTypeId, matched.Invoice.Matches[0].ServiceTypeId);
        policy.Available = false;
        Assert.True((await workflow.MatchAsync(invoice.Id, request, metadata)).IsReplay);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceInvoiceBindings WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.ServiceInvoiceBindings WHERE CompanyId=@company AND InvoiceId=@id "
            + "AND InvoiceLineId=@invoiceLine AND OrderId=@order AND LineId=@line AND AcceptanceId=@acceptance AND Quantity=4",
            ("@company", CompanyA), ("@id", invoice.Id), ("@invoiceLine", invoice.Lines[0].Id),
            ("@order", order.OrderId), ("@line", order.Lines[0].LineId), ("@acceptance", acceptance)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceReceiptBindings WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseInvoiceOrderBindings WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
    }

    [SqlServerFact]
    public async Task Service_invoice_matching_refuses_reused_acceptance_and_cross_company_sources_without_inventing_goods_receipts()
    {
        await using var run = await Run.StartAsync();
        var (order, acceptance) = await ServiceInvoiceAcceptanceAsync(run);
        var first = ServiceInvoice(run, order); var second = ServiceInvoice(run, order);
        await using var scope = run.Store(CompanyA);
        var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
        await store.RecordIncomingInvoiceAsync(first, run.Envelope("service-invoice-first"), Hash("service-invoice-first"), _ => Task.CompletedTask);
        await store.RecordIncomingInvoiceAsync(second, run.Envelope("service-invoice-second"), Hash("service-invoice-second"), _ => Task.CompletedTask);
        var workflow = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, new()));
        await workflow.MatchAsync(first.Id, new("1", [new(first.Lines[0].Id, order.OrderId, acceptance, "4")]),
            new("SENTETIK-service-first-match", run.Correlation, null, AcceptanceTime));
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => workflow.MatchAsync(second.Id,
            new("1", [new(second.Lines[0].Id, order.OrderId, acceptance, "4")]),
            new("SENTETIK-service-no-double-bill", run.Correlation, null, AcceptanceTime)));
        Assert.Equal(409, refusal.Status); Assert.Equal(PurchaseIncomingInvoice.SourceMismatch, refusal.Code);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, (await workflow.ReadAsync(second.Id)).Status);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceInvoiceBindings WHERE InvoiceId=@id", ("@id", second.Id)));
        await using var foreign = run.Store(CompanyB);
        Assert.Null(await ((IPurchasingIncomingInvoiceStoreV1)foreign.Store).FindIncomingInvoiceAsync(first.Id));
        var company = new CompanyContext(); company.Set(CompanyB);
        var foreignWorkspace = new PurchaseIncomingInvoiceWorkspace(foreign.Context, company,
            new NumberTestUser { UserId = run.Actor }, new NumberTestAuthority(), workflow);
        Assert.Empty((await foreignWorkspace.ListAsync(first.Source.OfficialNumber, 0)).Items);
    }

    [SqlServerFact]
    public async Task Service_price_completion_keeps_real_acceptance_history_and_matches_only_after_the_new_revision_is_approved()
    {
        await using var run = await Run.StartAsync();
        var (order, acceptance) = await ServiceInvoiceAcceptanceAsync(run, unpriced: true);
        var invoice = ServiceInvoice(run, order);
        await using (var scope = run.Store(CompanyA))
        {
            await ((IPurchasingIncomingInvoiceStoreV1)scope.Store).RecordIncomingInvoiceAsync(invoice,
                run.Envelope("service-late-price-invoice"), Hash("service-late-price-invoice"), _ => Task.CompletedTask);
            var service = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, new()));
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.MatchAsync(invoice.Id,
                new("1", [new(invoice.Lines[0].Id, order.OrderId, acceptance, "4")]),
                new("SENTETIK-service-missing-price", run.Correlation, null, AcceptanceTime)))).Status);
        }
        await using (var scope = ServiceScope(run, CompanyA))
        {
            var result = await scope.Store.WriteAsync(order.OrderId, PurchaseServiceOrderCommandsV1.RevisePrices,
                run.Envelope("service-later-price"), Hash("service-later-price"), "SENTETIK fiyat tamamlama",
                (old, _) => Task.FromResult(PurchaseServiceOrder.RevisePrices(old!, 3,
                    new Dictionary<Guid, decimal> { [order.Lines[0].LineId] = 2m }, "SENTETIK-price-preparer", "SENTETIK fiyat", old!.CreatedAtUtc)),
                _ => Task.CompletedTask, default);
            Assert.Equal(PurchaseServiceOrderStatus.Draft, result!.Order!.Status);
            await scope.Store.WriteAsync(order.OrderId, PurchaseServiceOrderCommandsV1.Approve,
                run.Envelope("service-later-reapproval"), Hash("service-later-reapproval"), "SENTETIK tekrar onay",
                (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 4, "SENTETIK-other-checker", old!.CreatedAtUtc)),
                _ => Task.CompletedTask, default);
        }
        await using (var scope = run.Store(CompanyA))
        {
            var service = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, new()));
            Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, (await service.MatchAsync(invoice.Id,
                new("1", [new(invoice.Lines[0].Id, order.OrderId, acceptance, "4")]),
                new("SENTETIK-service-real-price", run.Correlation, null, AcceptanceTime))).Invoice.Status);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceAcceptances WHERE OrderId=@id", ("@id", order.OrderId)));
        Assert.Equal(5, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderRevisions WHERE OrderId=@id", ("@id", order.OrderId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
    }

    private static async Task<(PurchaseServiceOrderState Order, Guid Acceptance)> ServiceInvoiceAcceptanceAsync(Run run, bool unpriced = false)
    {
        await using var scope = ServiceScope(run, CompanyA); var draft = ServiceDraft(run); var acceptance = Guid.NewGuid();
        if (unpriced) draft = draft with { Lines = [draft.Lines[0] with { UnitPrice = null }] };
        await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("service-source-" + draft.OrderId),
            Hash("service-source-" + draft.OrderId), "SENTETIK hizmet", (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
        await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve, run.Envelope("service-approve-" + draft.OrderId),
            Hash("service-approve-" + draft.OrderId), "SENTETIK onay", (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1,
                "SENTETIK-checker", old!.CreatedAtUtc)), _ => Task.CompletedTask, default);
        var result = await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Accept, run.Envelope("service-fact-" + draft.OrderId),
            Hash("service-fact-" + draft.OrderId), "SENTETIK kabul", (old, _) => Task.FromResult(PurchaseServiceOrder.Accept(old!, 2,
                acceptance, draft.Lines[0].LineId, 4m, Day, run.Actor, "SENTETIK gerçek kabul", old!.CreatedAtUtc)),
            _ => Task.CompletedTask, default);
        return (result!.Order!, acceptance);
    }
    private static PurchaseIncomingInvoice ServiceInvoice(Run run, PurchaseServiceOrderState order) => PurchaseIncomingInvoice.Record(Guid.NewGuid(),
        CompanyA, order.SupplierPartyId, order.CompanyAccountId, order.Money.CurrencyId, order.Money.CurrencyId, 2, 2, null,
        new(Guid.NewGuid(), 1, new string('b', 64), "SENTETIK-HIZ-" + Guid.NewGuid().ToString("N"), Guid.NewGuid(), Day), new(8m, 0m, 0m), null,
        [new(Guid.NewGuid(), 1, Guid.Empty, order.Lines[0].UnitId, 4m, "Service", "service", 0m, new(8m, 0m, 0m), null, order.Lines[0].ServiceTypeId)],
        [new(Guid.NewGuid(), Guid.NewGuid(), 1, Day.AddDays(30), 8m, null)], run.Actor, AcceptanceTime);
}
