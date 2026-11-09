using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A4c (MOD-03, SAT-D220; coordinator proof for #998) on real SQL Server: <see cref="PurchaseSalesLinkedSupplyReadPort"/> lists,
/// for one Sales order, the still-open commitments of APPROVED purchase orders reached through Sales-sourced request lines:
/// the Approved filter (Draft and Cancelled excluded), the join request line → order line (with the order's own request), the
/// technical slice folded into its general line, the OrderLineIds filter, a manual line never listed, another company never seen,
/// a read for another company refused, and nothing written (no audit, no version change). SENTETIK owners and identities.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_sales_linked_supply_read_lists_only_open_approved_commitments_of_the_order_and_writes_nothing()
    {
        await using Run run = await Run.StartAsync();
        Guid salesOrder = Guid.NewGuid(), salesLine1 = Guid.NewGuid(), salesLine2 = Guid.NewGuid();
        Guid otherSalesOrder = Guid.NewGuid(), otherSalesLine = Guid.NewGuid();

        // Company A: one approved request carrying two lines of the Sales order, a manual line and a line of another Sales order.
        Owners a = Owners.For(CompanyA);
        PurchaseRequest request = PurchaseRequest.Create(Guid.NewGuid(), a.Company, run.Actor, T0);
        Guid reqLine1 = Guid.NewGuid(), reqLine2 = Guid.NewGuid(), reqManual = Guid.NewGuid(), reqOther = Guid.NewGuid();
        request.AddLine(request.Version, reqLine1, Product, KgUnit, Kg(1000m), T0, PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, salesOrder, salesLine1));
        request.AddLine(request.Version, reqLine2, Product, KgUnit, Kg(1000m), T0, PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, salesOrder, salesLine2));
        request.AddLine(request.Version, reqManual, Product, KgUnit, Kg(1000m), T0);
        request.AddLine(request.Version, reqOther, Product, KgUnit, Kg(1000m), T0, PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, otherSalesOrder, otherSalesLine));
        Guid requestId = await StoreApprovedRequestAsync(run, a, request, "sl-a");

        Guid approvedOrder, draftOrder, cancelledOrder, manualOrder;
        Guid techLine = Guid.NewGuid();
        await using (StoreScope s = run.Store(CompanyA))
        {
            // Approved: SL1 100 kg (25 kg of it broken down to a technical slice), SL2 40 kg, the other Sales order's line 15 kg.
            PurchaseOrder o1 = PurchaseOrder.CreateForRequest(Guid.NewGuid(), a.Company, a.Party, requestId, T0);
            Guid general1 = Guid.NewGuid();
            o1.AddLine(o1.Version, general1, Product, KgUnit, Kg(100m), reqLine1, T0);
            o1.AddLine(o1.Version, Guid.NewGuid(), Product, KgUnit, Kg(40m), reqLine2, T0);
            o1.AddLine(o1.Version, Guid.NewGuid(), Product, KgUnit, Kg(15m), reqOther, T0);
            o1.BreakDown(o1.Version, general1, techLine, Product, Kg(25m), T0);
            approvedOrder = (await s.Store.StoreOrderAsync(run.Envelope("sl-o1"), Hash("sl-o1"), a.Account, Day, () => o1, VersionsOf(o1))).Order.Order.Id;
            await ApproveOrderAsync(run, s, approvedOrder, "sl-o1-approve");

            // Draft: never listed.
            PurchaseOrder o2 = Order(a, requestId, reqLine1, 10m);
            draftOrder = (await s.Store.StoreOrderAsync(run.Envelope("sl-o2"), Hash("sl-o2"), a.Account, Day, () => o2, VersionsOf(o2))).Order.Order.Id;

            // Cancelled: approved, then its whole remainder cancelled; never listed.
            PurchaseOrder o3 = Order(a, requestId, reqLine1, 20m);
            cancelledOrder = (await s.Store.StoreOrderAsync(run.Envelope("sl-o3"), Hash("sl-o3"), a.Account, Day, () => o3, VersionsOf(o3))).Order.Order.Id;
            await ApproveOrderAsync(run, s, cancelledOrder, "sl-o3-approve");
            await s.Store.MutateOrderAsync(run.Envelope("sl-o3-cancel"), Hash("sl-o3-cancel"), PurchasingCommands.OrderCancelV1, cancelledOrder,
                (order, now) => order.Cancel(order.Version, run.Actor, "SENTETIK iptal", now), OrderRemainderCancelled);

            // Approved, but its line comes from the manual request line: never listed.
            PurchaseOrder o4 = Order(a, requestId, reqManual, 7m);
            manualOrder = (await s.Store.StoreOrderAsync(run.Envelope("sl-o4"), Hash("sl-o4"), a.Account, Day, () => o4, VersionsOf(o4))).Order.Order.Id;
            await ApproveOrderAsync(run, s, manualOrder, "sl-o4-approve");
        }

        // Company B holds the SAME Sales order identity on its own approved order: company A never sees it.
        Owners b = Owners.For(CompanyB);
        PurchaseRequest requestB = PurchaseRequest.Create(Guid.NewGuid(), b.Company, run.Actor, T0);
        Guid reqLineB = Guid.NewGuid();
        requestB.AddLine(requestB.Version, reqLineB, Product, KgUnit, Kg(1000m), T0, PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, salesOrder, salesLine1));
        Guid requestBId = await StoreApprovedRequestAsync(run, b, requestB, "sl-b");
        Guid orderB;
        await using (StoreScope s = run.Store(CompanyB))
        {
            PurchaseOrder ob = Order(b, requestBId, reqLineB, 60m);
            orderB = (await s.Store.StoreOrderAsync(run.Envelope("sl-ob"), Hash("sl-ob"), b.Account, Day, () => ob, VersionsOf(ob))).Order.Order.Id;
            await ApproveOrderAsync(run, s, orderB, "sl-ob-approve");
        }

        long auditsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        long versionsBefore = await run.ScalarAsync<long>("SELECT COALESCE(SUM(Version), 0) FROM purchasing.PurchaseOrders");

        // As company A: the Sales order has exactly its two open commitments, from the approved order only.
        var all = await ReadLinkedSupplyAsync(run, CompanyA, CompanyA, salesOrder, null);
        Assert.Equal(2, all.Count);
        Assert.All(all, line => Assert.Equal((CompanyA, approvedOrder, "Approved"), (line.CompanyId, line.PurchaseOrderId, line.Status)));
        // 100 kg ordered on the general line (75 general + 25 technical) is ONE commitment of 100, not two lines.
        Assert.Equal("100", Assert.Single(all, line => line.SalesOrderLineId == salesLine1).OpenQuantityText);
        Assert.Equal("40", Assert.Single(all, line => line.SalesOrderLineId == salesLine2).OpenQuantityText);
        Assert.DoesNotContain(all, line => line.PurchaseOrderId == draftOrder || line.PurchaseOrderId == cancelledOrder || line.PurchaseOrderId == manualOrder || line.PurchaseOrderId == orderB);

        // The Sales line filter narrows the answer; an unknown line or Sales order has none.
        var one = Assert.Single(await ReadLinkedSupplyAsync(run, CompanyA, CompanyA, salesOrder, [salesLine2]));
        Assert.Equal((salesLine2, "40"), (one.SalesOrderLineId, one.OpenQuantityText));
        Assert.Empty(await ReadLinkedSupplyAsync(run, CompanyA, CompanyA, salesOrder, [Guid.NewGuid()]));
        Assert.Empty(await ReadLinkedSupplyAsync(run, CompanyA, CompanyA, Guid.NewGuid(), null));

        // The other Sales order of the same request is its own answer.
        var other = Assert.Single(await ReadLinkedSupplyAsync(run, CompanyA, CompanyA, otherSalesOrder, null));
        Assert.Equal((otherSalesLine, "15", approvedOrder), (other.SalesOrderLineId, other.OpenQuantityText, other.PurchaseOrderId));

        // As company B: only its own commitment on the same Sales order identity.
        var own = Assert.Single(await ReadLinkedSupplyAsync(run, CompanyB, CompanyB, salesOrder, null));
        Assert.Equal((CompanyB, orderB, salesLine1, "60"), (own.CompanyId, own.PurchaseOrderId, own.SalesOrderLineId, own.OpenQuantityText));

        // A read for another company than the resolved one is refused (deny-first), never answered with the other company's rows.
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadLinkedSupplyAsync(run, CompanyA, CompanyB, salesOrder, null));

        // A read creates nothing: no audit event, no version change.
        Assert.Equal(auditsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        Assert.Equal(versionsBefore, await run.ScalarAsync<long>("SELECT COALESCE(SUM(Version), 0) FROM purchasing.PurchaseOrders"));
    }

    private static async Task<Guid> StoreApprovedRequestAsync(Run run, Owners owners, PurchaseRequest built, string key)
    {
        long version = built.Version;
        await using StoreScope scope = run.Store(owners.Company);
        Guid id = (await scope.Store.StoreRequestAsync(run.Envelope($"{key}-req"), Hash($"{key}-req"), Day, () => built, VersionsOf(built))).Request.Request.Id;
        await scope.Store.MutateRequestAsync(run.Envelope($"{key}-submit"), Hash($"{key}-submit"), PurchasingCommands.RequestSubmitV1, id,
            (r, _, now) => r.Submit(version, run.Actor, now), Submitted);
        await ApproveRequestAsync(run, scope, id, version + 1, $"{key}-approve");
        return id;
    }

    private static async Task ApproveOrderAsync(Run run, StoreScope scope, Guid orderId, string key)
    {
        var approved = await scope.Store.MutateOrderAsync(run.Envelope(key), Hash(key), PurchasingCommands.OrderApproveV1, orderId,
            (order, now) => order.Approve(order.Version, now), Approved);
        Assert.Equal(PurchaseOrderStatus.Approved, approved!.Order.Order.Status);
    }

    /// <summary>The read port over its own context, resolved to <paramref name="resolved"/>, asked for <paramref name="asked"/>.</summary>
    private static async Task<IReadOnlyList<PurchaseSalesLinkedSupplyLineV1>> ReadLinkedSupplyAsync(
        Run run, Guid resolved, Guid asked, Guid salesOrder, IReadOnlyList<Guid>? lines)
    {
        CompanyContext company = new CompanyContext();
        company.Set(resolved);
        await using PurchasingDbContext context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .Options, company);
        return await new PurchaseSalesLinkedSupplyReadPort(context, company).ListOpenForSalesOrderAsync(asked, salesOrder, lines);
    }
}
