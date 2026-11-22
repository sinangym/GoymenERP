using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// W1-A (AI-D31/AI-D32): the purchase order Work inbox source lists the company's Draft orders only (what the approve command
/// accepts), oldest first, the maker's own included, with no invented threshold; it answers Denied without the company-wide
/// approve permission or for another company's context. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseOrderWorkInboxSourceV1Tests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_draft_orders_of_the_caller_company_are_listed_oldest_first_with_the_order_screen_link()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var older = Order(CompanyA, PurchaseOrderStatus.Draft, Start, "SENTETIK-PO-1");
        var newer = Order(CompanyA, PurchaseOrderStatus.Draft, Start.AddHours(2), null);
        await world.SeedAsync(CompanyA, db => db.Orders.AddRange(newer, older,
            Order(CompanyA, PurchaseOrderStatus.Approved, Start.AddMinutes(-5), "SENTETIK-PO-APPROVED"),
            Order(CompanyA, PurchaseOrderStatus.Cancelled, Start.AddMinutes(-6), "SENTETIK-PO-CANCELLED")));
        await world.SeedAsync(CompanyB, db => db.Orders.Add(Order(CompanyB, PurchaseOrderStatus.Draft, Start.AddMinutes(-9), "SENTETIK-PO-B")));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal(CompanyA, result.CompanyId);
        Assert.False(result.HasMore);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        AssertContractShape("purchasing", first);
        Assert.Equal("purchasing.order.v1", first.Kind);
        Assert.Equal("SENTETIK-PO-1", first.DocumentLabel);
        Assert.Equal("/satinalma/siparisler/" + older.Id.ToString("D"), first.Link);
        Assert.Equal(Start, first.WaitingSinceUtc);
        Assert.Equal("…" + newer.Id.ToString("D")[^8..], result.Items![1].DocumentLabel);
        Assert.Equal(PurchasingPermissionManifestV1.OrdersApprove, world.LastPermission);
    }

    [Fact]
    public async Task The_makers_own_draft_is_returned_and_the_rule_carries_no_invented_numbers()
    {
        var world = new PurchasingWorkInboxTestWorld { UserId = Maker };
        var own = Order(CompanyA, PurchaseOrderStatus.Draft, Start, "SENTETIK-PO-OWN");
        await world.SeedAsync(CompanyA, db => db.Orders.Add(own));

        var item = Assert.Single((await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None)).Items!);

        Assert.Equal(Maker, item.PreparedBy);
        var rule = Assert.Single(item.Rules);
        Assert.Equal(new WorkApprovalRuleResultV1(PurchaseOrderWorkInboxSourceV1.ApprovalRequiredRule, null, null, null, null), rule);
        Assert.Equal("supplier", Assert.Single(item.References).Role);
    }

    [Fact]
    public async Task A_draft_reopened_by_a_supplier_revision_waits_from_the_revision_and_names_the_revision_preparer()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var revisedAt = Start.AddDays(3);
        var row = Order(CompanyA, PurchaseOrderStatus.Draft, Start, "SENTETIK-PO-REV");
        var previousSupplier = Guid.NewGuid();
        row.CurrentPreparedBy = "SENTETIK-reviser";
        row.SupplierRevisionHistoryJson = JsonSerializer.Serialize(new PurchaseSupplierRevisionHistory(CompanyA, row.Id,
            [new PurchaseSupplierRevision(1, 3, previousSupplier, row.SupplierPartyId, Maker,
                new PurchaseOrderApproval(Approver, Start.AddDays(1)), 2, [], "SENTETIK-reviser", "SENTETIK neden", revisedAt)],
            [], null));
        await world.SeedAsync(CompanyA, db => db.Orders.Add(row));

        var item = Assert.Single((await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None)).Items!);

        Assert.Equal(revisedAt, item.WaitingSinceUtc);
        Assert.Equal("SENTETIK-reviser", item.PreparedBy);
        Assert.Equal([PurchaseOrderWorkInboxSourceV1.ApprovalRequiredRule, PurchaseOrderWorkInboxSourceV1.SupplierRevisedRule],
            item.Rules.Select(rule => rule.RuleCode));
    }

    [Fact]
    public async Task More_drafts_than_requested_set_has_more_and_return_only_the_oldest()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var rows = Enumerable.Range(0, 3).Select(index => Order(CompanyA, PurchaseOrderStatus.Draft, Start.AddMinutes(index), null)).ToArray();
        await world.SeedAsync(CompanyA, db => db.Orders.AddRange(rows));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal([rows[0].Id, rows[1].Id], result.Items!.Select(item => item.ItemId));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Without_the_company_wide_approve_permission_the_source_is_denied_without_items(bool granted, bool companyWide)
    {
        var world = new PurchasingWorkInboxTestWorld { Granted = granted, CompanyWide = companyWide };
        await world.SeedAsync(CompanyA, db => db.Orders.Add(Order(CompanyA, PurchaseOrderStatus.Draft, Start, "SENTETIK-PO")));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
    }

    [Fact]
    public async Task A_context_of_another_company_than_the_resolved_one_is_denied()
    {
        var world = new PurchasingWorkInboxTestWorld();
        await world.SeedAsync(CompanyB, db => db.Orders.Add(Order(CompanyB, PurchaseOrderStatus.Draft, Start, "SENTETIK-PO-B")));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyB), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
    }

    private static PurchaseOrderWorkInboxSourceV1 Source(PurchasingWorkInboxTestWorld world) =>
        new(world.Context(CompanyA), world.Fence(CompanyA));

    private static PurchaseOrderRow Order(Guid company, PurchaseOrderStatus status, DateTimeOffset createdAt, string? number) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, SupplierPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(),
        Status = (int)status, Version = 1, RowVersion = [1], EffectiveOn = new DateOnly(2026, 11, 2), CreatedAtUtc = createdAt,
        UpdatedAtUtc = createdAt, CreatedBy = Maker, CorrelationId = "SENTETIK-correlation", DocumentNumber = number,
    };
}
