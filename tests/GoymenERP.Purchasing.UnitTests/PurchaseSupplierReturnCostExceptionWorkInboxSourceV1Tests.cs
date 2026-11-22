using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// WP7 (AI-D31/AI-D32, SAT-D393/SAT-D396): the supplier return cost exception Work inbox source lists every
/// PendingCostException return of the company, oldest first, measured with the order approval permission; the manual unit cost
/// is shown only to a caller who may see purchase prices. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseSupplierReturnCostExceptionWorkInboxSourceV1Tests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_pending_returns_of_the_caller_company_are_listed_oldest_first_with_cost_and_kg()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-PO-R1");
        var older = Return(CompanyA, order.Id, PurchaseSupplierReturnStatus.PendingCostException, Start);
        var newer = Return(CompanyA, order.Id, PurchaseSupplierReturnStatus.PendingCostException, Start.AddMinutes(5));
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.SupplierReturns.AddRange(newer, older,
                Decided(Return(CompanyA, order.Id, PurchaseSupplierReturnStatus.Recorded, Start.AddDays(-1))));
        });
        var foreign = Order(CompanyB, "SENTETIK-PO-B");
        await world.SeedAsync(CompanyB, db =>
        {
            db.Orders.Add(foreign);
            db.SupplierReturns.Add(Return(CompanyB, foreign.Id, PurchaseSupplierReturnStatus.PendingCostException, Start.AddDays(-2)));
        });

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal([older.ReturnId, newer.ReturnId], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        AssertContractShape("purchasing", first);
        Assert.Equal("purchasing.supplier-return-cost-exception.v1", first.Kind);
        Assert.Equal("SENTETIK-PO-R1", first.DocumentLabel);
        Assert.Equal("/satinalma/siparisler/" + order.Id.ToString("D"), first.Link);
        Assert.Equal(Start, first.WaitingSinceUtc);
        Assert.Equal(Maker, first.PreparedBy);
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1(PurchaseSupplierReturnCostExceptionWorkInboxSourceV1.ManualCostRule, null, "42.5", null, "TRY"),
                new WorkApprovalRuleResultV1(PurchaseSupplierReturnCostExceptionWorkInboxSourceV1.QuantityRule, null, "1250.75", null, "kg"),
            ],
            first.Rules);
        Assert.Equal(("purchase-order", order.Id), (first.References[0].Role, first.References[0].RecordId));
        Assert.Contains(PurchasingPermissionManifestV1.OrdersApprove, world.AskedPermissions);
        Assert.Contains(PurchasingPermissionManifestV1.OrdersPriceRead, world.AskedPermissions);
    }

    [Fact]
    public async Task Without_price_read_the_manual_cost_rule_carries_no_number()
    {
        var world = new PurchasingWorkInboxTestWorld();
        world.Withheld.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var order = Order(CompanyA, "SENTETIK-PO-R2");
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.SupplierReturns.Add(Return(CompanyA, order.Id, PurchaseSupplierReturnStatus.PendingCostException, Start));
        });

        var item = Assert.Single((await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None)).Items!);

        Assert.Equal(new WorkApprovalRuleResultV1(PurchaseSupplierReturnCostExceptionWorkInboxSourceV1.ManualCostRule, null, null, null, null),
            item.Rules[0]);
        Assert.Equal("1250.75", item.Rules[1].Actual);
    }

    [Fact]
    public async Task A_pending_return_without_its_enterer_makes_the_read_unavailable()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-PO-R3");
        var row = Return(CompanyA, order.Id, PurchaseSupplierReturnStatus.PendingCostException, Start);
        row.RequestedBy = " ";
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.SupplierReturns.Add(row);
        });

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task More_pending_returns_than_requested_set_has_more_and_a_denied_caller_gets_no_items()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-PO-R4");
        var rows = Enumerable.Range(0, 3)
            .Select(index => Return(CompanyA, order.Id, PurchaseSupplierReturnStatus.PendingCostException, Start.AddMinutes(index))).ToArray();
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.SupplierReturns.AddRange(rows);
        });

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 2, CancellationToken.None);
        Assert.True(result.HasMore);
        Assert.Equal([rows[0].ReturnId, rows[1].ReturnId], result.Items!.Select(item => item.ItemId));

        world.Granted = false;
        var denied = await Source(world).ListPendingAsync(world.Caller(CompanyA), 2, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, denied.Outcome);
        Assert.Null(denied.Items);
    }

    private static PurchaseSupplierReturnCostExceptionWorkInboxSourceV1 Source(PurchasingWorkInboxTestWorld world) =>
        new(world.Context(CompanyA), world.Fence(CompanyA));

    private static PurchaseOrderRow Order(Guid company, string number) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, SupplierPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(),
        Status = (int)PurchaseOrderStatus.Approved, Version = 2, RowVersion = [1], EffectiveOn = new DateOnly(2026, 11, 1),
        CreatedAtUtc = Start.AddDays(-5), UpdatedAtUtc = Start.AddDays(-5), CreatedBy = "SENTETIK-buyer",
        CorrelationId = "SENTETIK-correlation", DocumentNumber = number,
    };

    private static PurchaseSupplierReturnRow Return(Guid company, Guid orderId, PurchaseSupplierReturnStatus status,
        DateTimeOffset requestedAt) => new()
    {
        CompanyId = company, ReturnId = Guid.NewGuid(), ReturnLineId = Guid.NewGuid(), OrderId = orderId, LineId = Guid.NewGuid(),
        GoodsAcceptanceId = Guid.NewGuid(), MovementId = Guid.NewGuid(), Status = (int)status, Version = 1, RowVersion = [1],
        SourceBlock = "SENTETIK", QuantityKg = 1250.75m, CostBasis = (int)PurchaseSupplierReturnCostBasis.ManualCostWithException,
        UnitCostTryPerKg = 42.5000m, Reason = "SENTETIK iade", OccurredAtUtc = requestedAt, RequestedBy = Maker,
        RequestedAtUtc = requestedAt, CorrelationId = "SENTETIK-correlation",
    };

    private static PurchaseSupplierReturnRow Decided(PurchaseSupplierReturnRow row)
    {
        row.Version = 2;
        row.DecidedBy = Approver;
        row.DecidedAtUtc = row.RequestedAtUtc.AddMinutes(1);
        return row;
    }
}
