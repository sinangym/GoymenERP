using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.WorkInbox;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingPlanWorkInboxSourceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(Maker)]
    public async Task Purpose_read_preserves_legacy_null_maker_native_version_and_approval_only_scope(string? maker)
    {
        Plan(Id(1), 1, 0, version: 7, createdBy: maker);
        authority.Answer = (permission, _) => permission == ManufacturingPermissionManifestV1.PlansApprove
            ? ManufacturingWorkInboxTestAuthority.CompanyWide : PermissionScope.Denied;
        var snapshot = await Source().ReadDecisionAsync(Context(), Id(1), default);
        Assert.NotNull(snapshot); Assert.Equal("7", snapshot.SourceVersion); Assert.True(snapshot.Pending);
        Assert.Equal(maker, snapshot.Item.PreparedBy);
        Assert.Equal("/onaylarim/manufacturing.plan.v1/" + Id(1).ToString("D"), snapshot.Item.Link);
        Assert.Equal(new WorkInboxItemScopeV1(null, null, Id(8001)), snapshot.Item.Scope);
        Assert.All(authority.Calls, call => Assert.Equal((ManufacturingPermissionManifestV1.PlansApprove, CompanyA), call));
        var listed = Assert.Single((await Source().ListPendingAsync(Context(), 10, default)).Items!);
        Assert.Equal("/uretim/plan/" + Id(1).ToString("D"), listed.Link); // The partial purpose is not published yet.
    }

    [Fact]
    public async Task Purpose_reapproval_retains_the_cancelled_order_reference_and_current_reviser_without_selecting_labels()
    {
        Plan(Id(1), 2, 0, version: 4);
        Revision(Id(1), 1, 3, 20);
        Order(Id(1), Id(91), 3, planVersionAfterCancel: 4, cancelledMinutes: 50);
        var snapshot = await Source().ReadDecisionAsync(Context(), Id(1), default);
        Assert.NotNull(snapshot); Assert.True(snapshot.Pending); Assert.Equal("4", snapshot.SourceVersion);
        Assert.Equal(Reviser, snapshot.Item.PreparedBy);
        Assert.Equal(ManufacturingPlanWorkInboxSourceV1.ReapprovalRule, Assert.Single(snapshot.Item.Rules).RuleCode);
        var reference = Assert.Single(snapshot.Item.References);
        Assert.Equal(("manufacturing-order", Id(91)), (reference.Role, reference.RecordId));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Terminal_purpose_snapshot_remains_readable_for_native_receipt_replay_without_a_false_pending_rule(int status)
    {
        Plan(Id(1), status, 0, version: 5, createdBy: null);
        var snapshot = await Source().ReadDecisionAsync(Context(), Id(1), default);
        Assert.NotNull(snapshot); Assert.False(snapshot.Pending); Assert.Equal("5", snapshot.SourceVersion);
        Assert.Null(snapshot.Item.PreparedBy); Assert.Empty(snapshot.Item.Rules);
    }

    [Fact]
    public async Task Missing_foreign_and_denied_purpose_reads_do_not_disclose_a_plan()
    {
        Plan(Id(1), 1, 0, companyId: CompanyB);
        Assert.Null(await Source().ReadDecisionAsync(Context(), Id(1), default));
        Assert.Null(await Source().ReadDecisionAsync(Context(), Id(2), default));
        Assert.Null(await Source().ReadDecisionAsync(Context(), Guid.Empty, default));
        Plan(Id(3), 1, 0);
        authority.Answer = (_, _) => PermissionScope.Denied;
        Assert.Null(await Source().ReadDecisionAsync(Context(), Id(3), default));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("status")]
    [InlineData("work-center")]
    public async Task Invalid_native_purpose_metadata_is_unavailable(string fault)
    {
        Plan(Id(1), 1, 0);
        var row = rows.PlanRows.Single();
        if (fault == "version") row.Version = 0;
        if (fault == "status") row.Status = 999;
        if (fault == "work-center") row.WorkCenterId = Guid.Empty;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source().ReadDecisionAsync(Context(), Id(1), default));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Fresh_authority_loss_after_any_purpose_query_discloses_no_snapshot(int revokedAt)
    {
        Plan(Id(1), 1, 0);
        authority.Answer = (_, _) => authority.Calls.Count < revokedAt
            ? ManufacturingWorkInboxTestAuthority.CompanyWide : PermissionScope.Denied;
        Assert.Null(await Source().ReadDecisionAsync(Context(), Id(1), default));
    }
}
