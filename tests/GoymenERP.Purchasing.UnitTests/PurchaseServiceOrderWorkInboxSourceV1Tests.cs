using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// WP7 (AI-D31/AI-D32, ALS-D29/D40): the service order Work inbox source lists every service order whose current revision is a
/// Draft, oldest change first, measured with the order approval permission; an approved order reopened by a price revision
/// says so. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseServiceOrderWorkInboxSourceV1Tests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_current_draft_service_orders_of_the_caller_company_are_listed_oldest_first()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var older = Draft(CompanyA, "SENTETIK-HS-1", Start);
        var newer = Draft(CompanyA, "SENTETIK-HS-2", Start.AddHours(1));
        var decided = Draft(CompanyA, "SENTETIK-HS-3", Start.AddDays(-1));
        var approved = PurchaseServiceOrder.Approve(decided, 1, Approver, Start.AddDays(-1));
        await world.SeedAsync(CompanyA, db => Add(db, [newer], [older], [decided, approved]));
        await world.SeedAsync(CompanyB, db => Add(db, [Draft(CompanyB, "SENTETIK-HS-B", Start.AddDays(-2))]));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.False(result.HasMore);
        Assert.Equal([older.OrderId, newer.OrderId], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        AssertContractShape("purchasing", first);
        Assert.Equal("purchasing.service-order.v1", first.Kind);
        Assert.Equal("SENTETIK-HS-1", first.DocumentLabel);
        Assert.Equal("/satinalma/hizmet-siparisleri/" + older.OrderId.ToString("D"), first.Link);
        Assert.Equal(Start, first.WaitingSinceUtc);
        Assert.Equal(Maker, first.PreparedBy);
        Assert.Equal([new WorkApprovalRuleResultV1(PurchaseServiceOrderWorkInboxSourceV1.ApprovalRequiredRule, null, null, null, null)],
            first.Rules);
        Assert.Equal(["supplier", "company-account"], first.References.Select(reference => reference.Role));
        Assert.Equal(PurchasingPermissionManifestV1.OrdersApprove, world.LastPermission);
    }

    [Fact]
    public async Task A_price_revision_reopens_an_approved_order_with_the_reviser_as_preparer()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var draft = Draft(CompanyA, "SENTETIK-HS-REV", Start);
        var approved = PurchaseServiceOrder.Approve(draft, 1, Approver, Start.AddMinutes(10));
        var revised = PurchaseServiceOrder.RevisePrices(approved, 2,
            new Dictionary<Guid, decimal> { [draft.Lines[0].LineId] = 3m }, "SENTETIK-reviser", "SENTETIK fiyat", Start.AddHours(3));
        await world.SeedAsync(CompanyA, db => Add(db, [draft, approved, revised]));

        var item = Assert.Single((await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None)).Items!);

        Assert.Equal("SENTETIK-reviser", item.PreparedBy);
        Assert.Equal(Start.AddHours(3), item.WaitingSinceUtc);
        Assert.Equal([PurchaseServiceOrderWorkInboxSourceV1.ApprovalRequiredRule, PurchaseServiceOrderWorkInboxSourceV1.PriceRevisedRule],
            item.Rules.Select(rule => rule.RuleCode));
    }

    [Fact]
    public async Task More_drafts_than_requested_set_has_more_and_a_denied_caller_gets_no_items()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var drafts = Enumerable.Range(0, 3).Select(index => Draft(CompanyA, "SENTETIK-HS-M" + index, Start.AddMinutes(index))).ToArray();
        await world.SeedAsync(CompanyA, db => Add(db, drafts.Select(draft => new[] { draft }).ToArray()));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 2, CancellationToken.None);
        Assert.True(result.HasMore);
        Assert.Equal([drafts[0].OrderId, drafts[1].OrderId], result.Items!.Select(item => item.ItemId));

        world.Granted = false;
        var denied = await Source(world).ListPendingAsync(world.Caller(CompanyA), 2, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, denied.Outcome);
        Assert.Null(denied.Items);
    }

    private static PurchaseServiceOrderWorkInboxSourceV1 Source(PurchasingWorkInboxTestWorld world) =>
        new(world.Context(CompanyA), world.Fence(CompanyA));

    /// <summary>Each array is one order's revision history, oldest first; the parent points at the last one.</summary>
    private static void Add(PurchasingDbContext db, params PurchaseServiceOrderState[][] histories)
    {
        foreach (var history in histories)
        {
            var current = history[^1];
            db.ServiceOrders.Add(new PurchaseServiceOrderRow { CompanyId = current.CompanyId, OrderId = current.OrderId, Version = current.Version });
            foreach (var revision in history)
            {
                db.ServiceOrderRevisions.Add(new PurchaseServiceOrderRevisionRow
                {
                    CompanyId = revision.CompanyId, OrderId = revision.OrderId, Version = revision.Version,
                    SnapshotJson = JsonSerializer.Serialize(revision, PurchaseServiceOrderStoreV1.Json),
                });
            }
        }
    }

    private static PurchaseServiceOrderState Draft(Guid company, string number, DateTimeOffset at) => PurchaseServiceOrder.Create(new(
        company, Guid.NewGuid(), 1, PurchaseServiceOrderKind.ServicePurchase, PurchaseServiceOrderStatus.Draft, Guid.NewGuid(),
        Guid.NewGuid(), "1", "1", new(2026, 11, 18), Maker, null, at, at, new(Guid.NewGuid(), "TRY", Guid.NewGuid(), 1, 2, 4),
        [new(Guid.NewGuid(), Guid.NewGuid(), 1, "SENTETIK hizmet", Guid.NewGuid(), "kg", Guid.NewGuid(), 1, 3, 10m, 2m, 0m)],
        [], [], [], number));
}
