using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class WorkPurchasingRequestDecisionHostTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Goods_receipt_purpose_forwards_native_order_batch_version_key_and_immutable_replay(bool approve, bool pending)
    {
        var run = new Run(goodsReceiptBatch: true); run.Source.Pending = pending;
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, default);
        Assert.Equal(pending, preview.Pending); Assert.False(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        var reason = approve ? null : new string('x', 500);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item,
            new("7", approve, reason), "SENTETIK-batch-key", default);
        Assert.Equal(approve ? "Posted" : "Rejected", result.State);
        Assert.True(result.Replayed); Assert.Equal("SENTETIK-original", result.OriginalCorrelationId);
        Assert.Equal(Item, run.BatchOrderArgument);
        var call = Assert.Single(run.Calls);
        Assert.Equal(approve ? "ApproveGoodsReceiptBatchAsync" : "RejectGoodsReceiptBatchAsync", call.Method);
        Assert.Equal(Item, call.Id); Assert.Equal(new DecidePurchaseGoodsReceiptBatchV1("7", reason), call.Body);
        Assert.Equal(("SENTETIK-batch-key", "SENTETIK-correlation"), (call.Metadata.IdempotencyKey, call.Metadata.CorrelationId));
        Assert.All(run.Raw.Asked, permission => Assert.Equal(PurchasingPermissionManifestV1.GoodsReceiptsApproveVariance, permission));
    }

    [Fact]
    public async Task Goods_receipt_optional_approval_reason_is_forwarded_and_native_approval_remains_reasonless()
    {
        var run = new Run(goodsReceiptBatch: true);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item,
            new("7", true, "SENTETIK optional native input"), "approve-reason", default);
        Assert.Equal("Posted", result.State);
        Assert.Equal("SENTETIK optional native input", Assert.IsType<DecidePurchaseGoodsReceiptBatchV1>(Assert.Single(run.Calls).Body).Reason);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(true, "overlong")]
    [InlineData(false, "overlong")]
    [InlineData(true, "control")]
    [InlineData(false, "control")]
    public async Task Goods_receipt_native_reason_validation_receives_the_exact_input(bool approve, string? input)
    {
        var run = new Run(goodsReceiptBatch: true);
        var reason = input == "overlong" ? new string('x', 501) : input == "control" ? "SENTETIK\0" : null;
        if (approve && input is null)
        {
            await run.Host.DecideAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, new("7", true, null), "valid-null", default);
        }
        else
        {
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
                WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, new("7", approve, reason), "invalid-reason", default))).Status);
            Assert.False(run.NativeEffect);
        }
        Assert.Equal(reason, Assert.IsType<DecidePurchaseGoodsReceiptBatchV1>(Assert.Single(run.Calls).Body).Reason);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("batch")]
    [InlineData("order")]
    [InlineData("pending")]
    [InlineData("opposite")]
    [InlineData("version")]
    [InlineData("canonical-version")]
    [InlineData("version-step")]
    [InlineData("decider")]
    [InlineData("decision-time")]
    [InlineData("zero-time")]
    [InlineData("offset-time")]
    [InlineData("reason")]
    public async Task Goods_receipt_foreign_or_malformed_native_success_is_never_presented_as_work_success(string fault)
    {
        var run = new Run(goodsReceiptBatch: true);
        run.BatchTransform = batch => fault switch
        {
            "company" => batch with { CompanyId = Guid.NewGuid() }, "batch" => batch with { BatchId = Guid.NewGuid() },
            "order" => batch with { PurchaseOrderId = Guid.NewGuid() }, "pending" => batch with { Status = "PendingApproval" },
            "opposite" => batch with { Status = "Rejected" }, "version" => batch with { Version = "1" },
            "version-step" => batch with { Version = "9" }, "canonical-version" => batch with { Version = "08" }, "decider" => batch with { DecidedBy = "SENTETIK-other" },
            "decision-time" => batch with { DecidedAtUtc = null }, "zero-time" => batch with { DecidedAtUtc = default(DateTimeOffset) },
            "offset-time" => batch with { DecidedAtUtc = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)) }, "reason" => batch with { DecisionReason = "SENTETIK incompatible" },
            _ => throw new NotSupportedException(),
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, new("7", true, null), "bad-receipt", default))).Status);
    }

    [Fact]
    public async Task Goods_receipt_native_rejection_reason_must_match_its_immutable_receipt_without_cutting_the_request()
    {
        var run = new Run(goodsReceiptBatch: true);
        run.BatchTransform = batch => batch with { DecisionReason = "SENTETIK other receipt" };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, new("7", false, "  SENTETIK red  "), "wrong-reason", default))).Status);
        Assert.Equal("  SENTETIK red  ", Assert.IsType<DecidePurchaseGoodsReceiptBatchV1>(Assert.Single(run.Calls).Body).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Goods_receipt_missing_or_duplicate_order_reference_never_reaches_native_owner(bool duplicate)
    {
        var run = new Run(goodsReceiptBatch: true);
        run.Source.BatchReferences = duplicate ? [new("purchase-order", "SENTETIK", Item, null), new("purchase-order", "SENTETIK", Guid.NewGuid(), null)] : [];
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, new("7", true, null), "bad-source", default))).Status);
        Assert.Empty(run.Calls);
    }

    [Fact]
    public async Task Goods_receipt_maker_denial_unavailable_owner_late_delegation_loss_and_terminal_refusal_keep_native_boundaries()
    {
        var run = new Run(goodsReceiptBatch: true); run.Source.Maker = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, default))).Status);
        Assert.Empty(run.Calls);
        run = new Run(goodsReceiptBatch: true, ownerAvailable: false);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, new("7", true, null), "missing-owner", default))).Status);
        Assert.Empty(run.Calls);
        run = new Run(goodsReceiptBatch: true); run.BeforeFence = () => run.Delegations.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, new("7", false, "SENTETIK red", Guid.NewGuid()), "late", default))).Status);
        Assert.False(run.NativeEffect);
        run = new Run(goodsReceiptBatch: true) { Refusal = new PurchasingServiceException(409, "version_conflict", isReplay: true, originalCorrelationId: "SENTETIK-first") };
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.PurchaseGoodsReceiptBatch, Item, new("7", false, "SENTETIK red"), "terminal", default));
        Assert.Equal((409, "version_conflict", true, "SENTETIK-first"), (error.Status, error.Code, error.Replayed, error.OriginalCorrelationId));
    }
}
