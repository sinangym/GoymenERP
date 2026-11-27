using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class WorkPurchasingRequestDecisionHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Service_order_purpose_calls_only_native_approval_with_the_human_reason_and_original_receipt(bool pending)
    {
        // The current preview may be Draft again after a revision, but native replay restores
        // the immutable Approved attempt snapshot rather than that current projection.
        var run = new Run(serviceOrder: true) { OrderState = "Approved" }; run.Source.Pending = pending;
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseServiceOrder, Item, default);
        Assert.True(preview.ApprovalReasonRequired); Assert.False(preview.RejectionSupported); Assert.Equal(pending, preview.Pending);
        const string reason = "  SENTETIK insanın gerçek onay gerekçesi  ";
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.PurchaseServiceOrder, Item, new("7", true, reason), "service-original-key", default);
        Assert.Equal("Approved", result.State); Assert.True(result.Replayed); Assert.Equal("SENTETIK-original", result.OriginalCorrelationId);
        var call = Assert.Single(run.Calls); Assert.Equal("ApproveAsync", call.Method); Assert.Equal(Item, call.Id);
        Assert.Equal(new ApprovePurchaseServiceOrderV1("7", reason), call.Body);
        Assert.Equal("service-original-key", call.Metadata.IdempotencyKey);
        Assert.All(run.Raw.Asked, permission => Assert.Equal(PurchasingPermissionManifestV1.OrdersApprove, permission));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("overlong")]
    public async Task Service_order_missing_or_overlong_human_reason_is_left_to_native_validation_without_a_default(string? reason)
    {
        var run = new Run(serviceOrder: true);
        if (reason == "overlong") reason = new string('x', 1001);
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseServiceOrder,
            Item, new("7", true, reason), "invalid-reason", default));
        Assert.Equal(422, error.Status);
        Assert.Equal(reason, Assert.IsType<ApprovePurchaseServiceOrderV1>(Assert.Single(run.Calls).Body).Reason);
        Assert.False(run.NativeEffect);
    }

    [Fact]
    public async Task Service_order_native_thousand_character_reason_is_not_cut_to_a_generic_five_hundred_character_limit()
    {
        var run = new Run(serviceOrder: true);
        var reason = " " + new string('x', 1000) + " ";
        await run.Host.DecideAsync(WorkDecisionHostV1.PurchaseServiceOrder, Item, new("7", true, reason), "native-limit", default);
        Assert.Equal(reason, Assert.IsType<ApprovePurchaseServiceOrderV1>(Assert.Single(run.Calls).Body).Reason);
    }

    [Fact]
    public async Task Service_order_rejection_and_missing_owner_never_use_an_alternate_native_command()
    {
        var run = new Run(serviceOrder: true);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseServiceOrder,
            Item, new("7", false, "SENTETIK ret"), "no-reject", default))).Status);
        Assert.Empty(run.Calls);
        run = new Run(serviceOrder: true, ownerAvailable: false);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseServiceOrder,
            Item, new("7", true, "SENTETIK onay"), "missing", default))).Status);
        Assert.Empty(run.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Service_order_foreign_native_response_is_never_presented_as_success(bool foreignCompany)
    {
        var run = new Run(serviceOrder: true);
        if (foreignCompany) run.ResultCompany = Guid.NewGuid(); else run.ResultId = Guid.NewGuid();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseServiceOrder,
            Item, new("7", true, "SENTETIK onay"), "foreign", default))).Status);
    }

    [Fact]
    public async Task Service_order_maker_and_late_delegation_loss_are_refused_and_native_terminal_receipts_are_preserved()
    {
        var run = new Run(serviceOrder: true); run.Source.Maker = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseServiceOrder, Item, default))).Status);
        Assert.Empty(run.Calls);
        run = new Run(serviceOrder: true); run.BeforeFence = () => run.Delegations.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseServiceOrder,
            Item, new("7", true, "SENTETIK onay", Guid.NewGuid()), "late", default))).Status);
        Assert.False(run.NativeEffect); Assert.True(run.Delegations.Reads >= 2);
        run = new Run(serviceOrder: true) { Refusal = new PurchasingServiceException(409, "version_conflict", isReplay: true, originalCorrelationId: "SENTETIK-first") };
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseServiceOrder,
            Item, new("7", true, "SENTETIK onay"), "terminal", default));
        Assert.Equal((409, "version_conflict", true, "SENTETIK-first"), (error.Status, error.Code, error.Replayed, error.OriginalCorrelationId));
    }
}
