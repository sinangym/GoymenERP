using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class WorkPurchasingRequestDecisionHostTests
{
    [Theory]
    [InlineData("Approved")]
    [InlineData("Draft")]
    [InlineData("Cancelled")]
    public async Task Native_order_approval_replay_keeps_its_original_version_key_and_current_owner_projection(string state)
    {
        var run = new Run(order: true) { OrderState = state }; run.Source.Pending = false;
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseOrder, Item, default);
        Assert.False(preview.RejectionSupported); Assert.False(preview.Pending);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.PurchaseOrder, Item, new("7", true, null), "original-key", default);
        Assert.Equal(state, result.State); Assert.True(result.Replayed); Assert.Equal("SENTETIK-original", result.OriginalCorrelationId);
        var call = Assert.Single(run.Calls); Assert.Equal("ApproveOrderAsync", call.Method); Assert.Equal(Item, call.Id);
        Assert.Equal(new ApprovePurchaseOrderRequest("7"), call.Body);
        Assert.Equal(("original-key", "SENTETIK-correlation"), (call.Metadata.IdempotencyKey, call.Metadata.CorrelationId));
        Assert.All(run.Raw.Asked, permission => Assert.Equal(PurchasingPermissionManifestV1.OrdersApprove, permission));
    }

    [Fact]
    public async Task Purchase_order_rejection_never_forwards_a_cancel_or_another_native_command()
    {
        var run = new Run(order: true);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseOrder,
            Item, new("7", false, "SENTETIK ret"), "no-reject", default))).Status);
        Assert.Empty(run.Calls); Assert.False(run.NativeEffect);
    }

    [Fact]
    public async Task Order_maker_foreign_snapshot_or_denied_authority_never_reaches_native_approval()
    {
        var run = new Run(order: true); run.Source.Maker = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseOrder, Item, default))).Status);
        run.Source.Maker = "SENTETIK-maker"; run.Source.CompanyId = Guid.NewGuid();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseOrder, Item, default))).Status);
        run.Source.CompanyId = Company; run.Raw.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseOrder, Item, new("7", true, null), "denied", default))).Status);
        Assert.Empty(run.Calls);
    }

    [Fact]
    public async Task Order_approval_preserves_native_terminal_replay_refusals_and_rechecks_delegation()
    {
        var run = new Run(order: true) { Refusal = new PurchasingServiceException(409, "version_conflict", isReplay: true, originalCorrelationId: "SENTETIK-first") };
        var refused = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseOrder,
            Item, new("7", true, null), "same-key", default));
        Assert.Equal((409, "version_conflict", true, "SENTETIK-first"), (refused.Status, refused.Code, refused.Replayed, refused.OriginalCorrelationId));
        run = new Run(order: true); run.BeforeFence = () => run.Delegations.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseOrder,
            Item, new("7", true, null, Guid.NewGuid()), "late", default))).Status);
        Assert.False(run.NativeEffect); Assert.True(run.Delegations.Reads >= 2);
    }

    [Fact]
    public async Task Missing_native_order_owner_fails_closed_without_an_alternate_command_path()
    {
        var run = new Run(order: true, ownerAvailable: false);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseOrder,
            Item, new("7", true, null), "missing-owner", default))).Status);
        Assert.Empty(run.Calls); Assert.False(run.NativeEffect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_native_order_response_with_another_company_or_record_is_not_presented_as_success(bool foreignCompany)
    {
        var run = new Run(order: true);
        if (foreignCompany) run.ResultCompany = Guid.NewGuid(); else run.ResultId = Guid.NewGuid();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseOrder,
            Item, new("7", true, null), "foreign-result", default))).Status);
    }
}
