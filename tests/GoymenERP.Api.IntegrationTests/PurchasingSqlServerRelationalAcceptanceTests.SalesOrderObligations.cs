using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Sales_source_request_is_open_before_sourcing_and_cancelled_po_shares_remain_used_under_ALS_D68()
    {
        await using var run = await Run.StartAsync();
        var salesOrderId = Guid.NewGuid(); var salesLineId = Guid.NewGuid();
        var owners = Owners.For(CompanyA);
        var built = PurchaseRequest.Create(Guid.NewGuid(), CompanyA, run.Actor, T0);
        built.AddLine(built.Version, Guid.NewGuid(), Product, KgUnit, Kg(10), T0,
            PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, salesOrderId, salesLineId));
        var requestId = built.Id;
        var requestLineId = built.Lines[0].Id;
        await using (var scope = run.Store(CompanyA))
            await scope.Store.StoreRequestAsync(run.Envelope("obligation-request"), Hash("obligation-request"), Day, () => built, VersionsOf(built));
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Open, (await ReadObligation(run, salesOrderId)).Status);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.MutateRequestAsync(run.Envelope("obligation-submit"), Hash("obligation-submit"), PurchasingCommands.RequestSubmitV1,
                requestId, (request, _, now) => request.Submit(2, run.Actor, now), Submitted);
            await ApproveRequestAsync(run, scope, requestId, 3, "obligation-approve-request");
        }
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Open, (await ReadObligation(run, salesOrderId)).Status);
        foreach (var share in new[] { 4m, 6m })
        {
            var order = Order(owners, requestId, requestLineId, share);
            await using (var scope = run.Store(CompanyA))
            {
                var prefix = $"obligation-po-{share}";
                await scope.Store.StoreOrderAsync(run.Envelope(prefix), Hash(prefix), owners.Account, Day, () => order, VersionsOf(order));
                await scope.Store.MutateOrderAsync(run.Envelope(prefix + "-approve", actor: OrderApprover), Hash(prefix + "-approve"),
                    PurchasingCommands.OrderApproveV1, order.Id, (target, now) => target.Approve(2, OrderApprover, now), Approved);
                await scope.Store.MutateOrderAsync(run.Envelope(prefix + "-cancel"), Hash(prefix + "-cancel"),
                    PurchasingCommands.OrderCancelV1, order.Id, (target, now) => target.Cancel(3, run.Actor, "SENTETIK", now), OrderRemainderCancelled);
            }
            Assert.Equal(share == 4 ? PurchaseSalesOrderObligationStatusV1.Open : PurchaseSalesOrderObligationStatusV1.Complete,
                (await ReadObligation(run, salesOrderId)).Status);
        }
        var wrongCompany = await ReadObligation(run, salesOrderId, companyId: CompanyB);
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Unavailable, wrongCompany.Status);
        var revoked = await ReadObligation(run, salesOrderId, revokeAfterFirstRead: true);
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Unavailable, revoked.Status);
    }

    [SqlServerFact]
    public async Task A_shared_po_other_sales_order_share_does_not_block_the_finished_target_share()
    {
        await using var run = await Run.StartAsync();
        var salesOrderId = Guid.NewGuid(); var otherSalesOrderId = Guid.NewGuid();
        var owners = Owners.For(CompanyA);
        var request = PurchaseRequest.Create(Guid.NewGuid(), CompanyA, run.Actor, T0);
        var target = request.AddLine(request.Version, Guid.NewGuid(), Product, KgUnit, Kg(10), T0,
            PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, salesOrderId, Guid.NewGuid()));
        var other = request.AddLine(request.Version, Guid.NewGuid(), Product, KgUnit, Kg(20), T0,
            PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, otherSalesOrderId, Guid.NewGuid()));
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.StoreRequestAsync(run.Envelope("shared-req"), Hash("shared-req"), Day, () => request, VersionsOf(request));
            await scope.Store.MutateRequestAsync(run.Envelope("shared-submit"), Hash("shared-submit"), PurchasingCommands.RequestSubmitV1,
                request.Id, (value, _, now) => value.Submit(3, run.Actor, now), Submitted);
            await ApproveRequestAsync(run, scope, request.Id, 4, "shared-approve-req");
        }
        var order = Order(owners, request.Id, target.Id, 10);
        var targetLineId = order.Lines[0].Id;
        order.AddLine(order.Version, Guid.NewGuid(), Product, KgUnit, Kg(20), other.Id, T0);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.StoreOrderAsync(run.Envelope("shared-po"), Hash("shared-po"), owners.Account, Day, () => order, VersionsOf(order));
            await scope.Store.MutateOrderAsync(run.Envelope("shared-approve", actor: OrderApprover), Hash("shared-approve"),
                PurchasingCommands.OrderApproveV1, order.Id, (value, now) => value.Approve(3, OrderApprover, now), Approved);
            await scope.Store.MutateOrderAsync(run.Envelope("shared-close"), Hash("shared-close"), PurchasingCommands.OrderLineCloseV1,
                order.Id, (value, now) => value.CloseLine(4, targetLineId, run.Actor, "SENTETIK", now), LineClosed);
        }
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Complete, (await ReadObligation(run, salesOrderId)).Status);
        Assert.Equal(PurchaseSalesOrderObligationStatusV1.Open, (await ReadObligation(run, otherSalesOrderId)).Status);
    }

    private static async Task<PurchaseSalesOrderObligationReadV1> ReadObligation(Run run, Guid salesOrderId,
        Guid? companyId = null, bool revokeAfterFirstRead = false)
    {
        await using var scope = run.Store(CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var calls = 0;
        var answer = await new PurchaseSalesOrderObligationReadPortV1(scope.Context, company).ReadAsync(companyId ?? CompanyA, salesOrderId, _ =>
        {
            if (++calls == 3 && revokeAfterFirstRead) throw new UnauthorizedAccessException();
            return Task.CompletedTask;
        });
        Assert.False(scope.Context.ChangeTracker.HasChanges());
        Assert.Equal(salesOrderId, answer.SalesOrderId);
        Assert.Equal(companyId ?? CompanyA, answer.CompanyId);
        return answer;
    }
}
