using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseIncomingInvoiceVarianceWorkInboxSourceV1Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Purpose_pending_depends_on_the_variance_decision_not_the_matched_status(bool pending)
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-PO"); order.ApprovedBy = "SENTETIK-order-approver"; order.ApprovedAtUtc = Start.AddDays(-1); order.Version = 3;
        var (invoice, revisions) = Matched(CompanyA, "SENTETIK-FARK", order.Id, 50m, Start);
        if (!pending)
        {
            invoice.ApproveVariance(invoice.Version, "SENTETIK-accounting-checker", "SENTETIK gerçek gerekçe", Start.AddMinutes(1));
            revisions.Add(Revision(invoice, Start.AddMinutes(1)));
        }
        await world.SeedAsync(CompanyA, db => { db.Orders.Add(order); db.IncomingInvoiceRevisions.AddRange(revisions); });
        var source = Assert.IsAssignableFrom<IWorkApprovalDecisionSourceV1>(Source(world));
        var answer = await source.ReadDecisionAsync(world.Caller(CompanyA), invoice.Id, default);
        Assert.NotNull(answer); Assert.Equal(pending, answer.Pending);
        Assert.Equal(pending ? "2" : "3", answer.SourceVersion);
        Assert.Equal("Matched", invoice.Status.ToString());
        Assert.True(answer.Item.Excludes("SENTETIK-order-approver"));
        Assert.True(answer.Item.Excludes(Matcher));
        Assert.True(answer.Item.Excludes(invoice.RecordedBy));
        Assert.Contains(PurchasingPermissionManifestV1.OrdersPriceRead, world.AskedPermissions);
        Assert.Equal("/onaylarim/purchasing.incoming-invoice-variance.v1/" + invoice.Id.ToString("D"), answer.Item.Link);
    }

    [Fact]
    public async Task Purpose_cannot_disclose_foreign_or_price_denied_invoice_facts()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-PO");
        var (invoice, revisions) = Matched(CompanyA, "SENTETIK-FARK", order.Id, 50m, Start);
        await world.SeedAsync(CompanyA, db => { db.Orders.Add(order); db.IncomingInvoiceRevisions.AddRange(revisions); });
        Assert.Null(await Source(world).ReadDecisionAsync(world.Caller(CompanyA), Guid.NewGuid(), default));
        world.Withheld.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        Assert.Null(await Source(world).ReadDecisionAsync(world.Caller(CompanyA), invoice.Id, default));
    }

    [Fact]
    public async Task Purpose_refuses_a_matched_order_missing_from_the_company_instead_of_omitting_its_exclusion()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var (invoice, revisions) = Matched(CompanyA, "SENTETIK-MISSING-PO", Guid.NewGuid(), 50m, Start);
        await world.SeedAsync(CompanyA, db => db.IncomingInvoiceRevisions.AddRange(revisions));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source(world).ReadDecisionAsync(world.Caller(CompanyA), invoice.Id, default));
    }

    [Fact]
    public async Task Purpose_never_treats_a_null_order_approver_as_an_empty_exclusion()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-UNKNOWN"); order.Version = 3;
        order.ApprovedBy = null; order.ApprovedAtUtc = null;
        var (invoice, revisions) = Matched(CompanyA, "SENTETIK-UNKNOWN", order.Id, 50m, Start);
        await world.SeedAsync(CompanyA, db => { db.Orders.Add(order); db.IncomingInvoiceRevisions.AddRange(revisions); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source(world).ReadDecisionAsync(world.Caller(CompanyA), invoice.Id, default));
        var listed = await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, listed.Outcome);
        Assert.Null(listed.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Purpose_uses_the_approval_of_the_frozen_order_version_after_a_supplier_revision_or_reapproval(bool reapproved)
    {
        var world = new PurchasingWorkInboxTestWorld();
        var row = Order(CompanyA, "SENTETIK-HISTORY");
        var unit = Guid.NewGuid(); var line = Guid.NewGuid();
        var order = PurchaseOrder.Create(row.Id, CompanyA, row.SupplierPartyId,
            PurchaseOrderMoney.Of(Guid.NewGuid(), "TRY", Guid.NewGuid(), 1, 2), Start.AddDays(-4), "SENTETIK-buyer");
        order.AddLine(1, line, Guid.NewGuid(), unit, Quantity.Of(10m, QuantityUnit.Kilogram), Start.AddDays(-4));
        order.Approve(2, "SENTETIK-original-order-approver", Start.AddDays(-3));
        order.ReviseSupplier(3, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, "SENTETIK-reviser", "SENTETIK revision", Start.AddDays(-2));
        if (reapproved)
        {
            order.SetSupplierRevisionPrice(order.Version, line, new(3m, unit), "SENTETIK-reviser", Start.AddDays(-1));
            order.Approve(order.Version, "SENTETIK-new-order-approver", Start);
        }
        row.SupplierPartyId = order.SupplierPartyId; row.CurrentPreparedBy = order.PreparedBy;
        row.Version = order.Version; row.Status = (int)order.Status;
        row.ApprovedBy = order.Approval?.ApprovedBy; row.ApprovedAtUtc = order.Approval?.ApprovedAtUtc;
        row.SupplierRevisionHistoryJson = PurchaseSupplierRevisionHistory.Write(order);
        // Owner history resolver fixture only: the match below is synthetic. This does not claim that a
        // native receipt/invoice permits a supplier revision (the native lifecycle blocks that revision).
        var (invoice, revisions) = Matched(CompanyA, "SENTETIK-HISTORY", row.Id, 50m, Start);
        await world.SeedAsync(CompanyA, db => { db.Orders.Add(row); db.IncomingInvoiceRevisions.AddRange(revisions); });
        var answer = await Source(world).ReadDecisionAsync(world.Caller(CompanyA), invoice.Id, default);
        Assert.NotNull(answer);
        Assert.True(answer.Item.Excludes("SENTETIK-original-order-approver"));
        var listed = Assert.Single((await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, default)).Items!);
        Assert.True(listed.Excludes("SENTETIK-original-order-approver"));
        Assert.Equal(answer.Item.ExcludedDeciders, listed.ExcludedDeciders);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Variance_amounts_are_withheld_without_current_price_permission_before_or_after_the_list_read(bool revokeAfterRead)
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-PRICE-FENCE");
        var (_, revisions) = Matched(CompanyA, "SENTETIK-PRICE-FENCE", order.Id, 50m, Start);
        await world.SeedAsync(CompanyA, db => { db.Orders.Add(order); db.IncomingInvoiceRevisions.AddRange(revisions); });
        if (!revokeAfterRead) world.Withheld.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        else world.BeforePermissionAnswer = permission =>
        {
            if (permission == PurchasingPermissionManifestV1.OrdersPriceRead
                && world.AskedPermissions.Count(value => value == permission) == 2)
                world.Withheld.Add(permission);
        };
        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
        Assert.Equal(revokeAfterRead ? 2 : 1,
            world.AskedPermissions.Count(value => value == PurchasingPermissionManifestV1.OrdersPriceRead));
    }
}
