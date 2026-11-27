using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseGoodsReceiptBatchWorkInboxSourceV1Tests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public async Task Purpose_read_keeps_native_version_order_reference_maker_and_terminal_receipt_access(int status, bool pending)
    {
        var world = new PurchasingWorkInboxTestWorld();
        world.Withheld.Add(PurchasingPermissionManifestV1.OrdersRead);
        world.Withheld.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var order = Order(CompanyA, "SENTETIK-PO-purpose");
        var row = Batch(CompanyA, order.Id, (PurchaseGoodsReceiptBatchStatus)status, Start);
        if (!pending) { Decided(row); row.Version = 2; }
        var label = PurposeLabel(row);
        await world.SeedAsync(CompanyA, db => { db.Orders.Add(order); db.GoodsReceiptBatches.Add(row); db.GoodsReceiptBatchLabels.Add(label); });
        var result = await Source(world).ReadDecisionAsync(world.Caller(CompanyA), row.BatchId, default);
        Assert.NotNull(result);
        Assert.Equal((CompanyA, pending, pending ? "1" : "2"), (result.CompanyId, result.Pending, result.SourceVersion));
        Assert.Equal(Maker, result.Item.PreparedBy);
        Assert.True(result.Item.Excludes(Maker));
        Assert.Equal("/onaylarim/purchasing.goods-receipt-batch.v1/" + row.BatchId.ToString("D"), result.Item.Link);
        Assert.Equal(order.Id, Assert.Single(result.Item.References, reference => reference.Role == "purchase-order").RecordId);
        Assert.Equal(label.Released ? 1 : 2, result.Item.References.Count);
        if (label.Released) Assert.DoesNotContain(result.Item.References, reference => reference.Role == "goods-receipt-label");
        else Assert.Equal(label.LabelId, Assert.Single(result.Item.References, reference => reference.Role == "goods-receipt-label").RecordId);
        Assert.All(world.AskedPermissions, permission => Assert.Equal(PurchasingPermissionManifestV1.GoodsReceiptsApproveVariance, permission));
    }

    [Fact]
    public async Task Posted_at_submission_is_readable_but_never_pending_or_misrepresented_as_an_approval_receipt()
    {
        var world = new PurchasingWorkInboxTestWorld(); var order = Order(CompanyA, "SENTETIK-PO-auto");
        var row = Batch(CompanyA, order.Id, PurchaseGoodsReceiptBatchStatus.Posted, Start);
        await world.SeedAsync(CompanyA, db => { db.Orders.Add(order); db.GoodsReceiptBatches.Add(row); db.GoodsReceiptBatchLabels.Add(PurposeLabel(row)); });
        var result = await Source(world).ReadDecisionAsync(world.Caller(CompanyA), row.BatchId, default);
        Assert.NotNull(result); Assert.False(result.Pending); Assert.Equal("1", result.SourceVersion);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("version")]
    [InlineData("order")]
    [InlineData("decision")]
    [InlineData("terminal-maker")]
    [InlineData("labels")]
    [InlineData("label-total")]
    [InlineData("label-source")]
    public async Task Malformed_purpose_projection_is_unavailable_instead_of_partial_success(string fault)
    {
        var world = new PurchasingWorkInboxTestWorld(); var order = Order(CompanyA, "SENTETIK-PO-malformed");
        var row = Batch(CompanyA, order.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start);
        if (fault == "status") row.Status = 777;
        if (fault == "version") row.Version = 0;
        if (fault == "order") row.OrderId = Guid.NewGuid();
        if (fault == "decision") row.DecidedBy = Approver;
        if (fault == "terminal-maker") { row.Status = 3; row.Version = 2; Decided(row); row.DecidedBy = Maker; }
        // Synthetic corrupt persisted shape, not a lawful mutation of an immutable native label.
        // Seed the negative fixture once; never delete or rewrite an existing label to arrange it.
        var label = PurposeLabel(row);
        if (fault == "label-total") label.MeasuredQuantityKg = 1m;
        if (fault == "label-source") label.CodeSource = 777;
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order); db.GoodsReceiptBatches.Add(row);
            if (fault != "labels") db.GoodsReceiptBatchLabels.Add(label);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source(world).ReadDecisionAsync(world.Caller(CompanyA), row.BatchId, default));
    }

    [Fact]
    public async Task Foreign_missing_empty_and_permission_denied_purpose_reads_disclose_no_batch()
    {
        var world = new PurchasingWorkInboxTestWorld(); var foreign = Order(CompanyB, "SENTETIK-PO-B");
        var row = Batch(CompanyB, foreign.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start);
        await world.SeedAsync(CompanyB, db => { db.Orders.Add(foreign); db.GoodsReceiptBatches.Add(row); db.GoodsReceiptBatchLabels.Add(PurposeLabel(row)); });
        Assert.Null(await Source(world).ReadDecisionAsync(world.Caller(CompanyA), row.BatchId, default));
        Assert.Null(await Source(world).ReadDecisionAsync(world.Caller(CompanyA), Guid.NewGuid(), default));
        Assert.Null(await Source(world).ReadDecisionAsync(world.Caller(CompanyA), Guid.Empty, default));
        world.Granted = false;
        Assert.Null(await Source(world).ReadDecisionAsync(world.Caller(CompanyA), row.BatchId, default));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Approval_authority_is_checked_after_each_purpose_read(int revokedAt)
    {
        var world = new PurchasingWorkInboxTestWorld(); var order = Order(CompanyA, "SENTETIK-PO-fresh");
        var row = Batch(CompanyA, order.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start);
        await world.SeedAsync(CompanyA, db => { db.Orders.Add(order); db.GoodsReceiptBatches.Add(row); db.GoodsReceiptBatchLabels.Add(PurposeLabel(row)); });
        var company = new CompanyContext(); company.Set(CompanyA);
        var source = new PurchaseGoodsReceiptBatchWorkInboxSourceV1(world.Context(CompanyA),
            new PurchasingWorkInboxFence(company, new PurposeUser(), new PurposeAuthority(revokedAt)));
        Assert.Null(await source.ReadDecisionAsync(world.Caller(CompanyA), row.BatchId, default));
    }

    private static PurchaseGoodsReceiptBatchLabelRow PurposeLabel(PurchaseGoodsReceiptBatchRow row) => new()
    {
        CompanyId = row.CompanyId, BatchId = row.BatchId, LineNo = 1, AcceptanceId = Guid.NewGuid(), LotId = Guid.NewGuid(),
        LabelId = Guid.NewGuid(), MovementId = Guid.NewGuid(), LabelCode = "SENTETIK-LABEL", CodeSource = (int)GoodsReceiptLabelCodeSource.Entered,
        MeasuredQuantityKg = row.ReceivedQuantityKg, Released = row.Status == (int)PurchaseGoodsReceiptBatchStatus.Rejected,
    };

    private sealed class PurposeUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Approver;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyA]);
        public bool CanAccessCompany(Guid companyId) => companyId == CompanyA;
    }
    private sealed class PurposeAuthority(int revokedAt) : IErpAuthorityRevalidationService
    {
        private int checks;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(++checks < revokedAt, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
}
