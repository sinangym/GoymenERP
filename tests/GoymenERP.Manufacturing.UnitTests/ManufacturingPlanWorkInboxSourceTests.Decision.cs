using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.WorkInbox;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

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
        Assert.Null(snapshot.ManufacturingPlanReapproval);
        Assert.Equal("/onaylarim/manufacturing.plan.v1/" + Id(1).ToString("D"), snapshot.Item.Link);
        Assert.Equal(new WorkInboxItemScopeV1(null, null, Id(8001)), snapshot.Item.Scope);
        Assert.All(authority.Calls, call => Assert.Equal((ManufacturingPermissionManifestV1.PlansApprove, CompanyA), call));
        var listed = Assert.Single((await Source().ListPendingAsync(Context(), 10, default)).Items!);
        Assert.Equal("/onaylarim/manufacturing.plan.v1/" + Id(1).ToString("D"), listed.Link);
    }

    [Fact]
    public async Task Purpose_reapproval_retains_the_cancelled_order_reference_and_current_reviser_without_selecting_labels()
    {
        Plan(Id(1), 2, 0, version: 4);
        Revision(Id(1), 1, 3, 20);
        rows.RevisionInputRows.Add(new() { CompanyId = CompanyA, PlanId = Id(1), RevisionNo = 1, ParentRollId = Id(71) });
        Order(Id(1), Id(91), 3, planVersionAfterCancel: 4, cancelledMinutes: 50);
        var snapshot = await Source().ReadDecisionAsync(Context(), Id(1), default);
        Assert.NotNull(snapshot); Assert.True(snapshot.Pending); Assert.Equal("4", snapshot.SourceVersion);
        Assert.Equal(Reviser, snapshot.Item.PreparedBy);
        Assert.Equal(ManufacturingPlanWorkInboxSourceV1.ReapprovalRule, Assert.Single(snapshot.Item.Rules).RuleCode);
        var reference = Assert.Single(snapshot.Item.References);
        Assert.Equal(("manufacturing-order", Id(91)), (reference.Role, reference.RecordId));
        Assert.Equal([Id(71)], snapshot.ManufacturingPlanReapproval!.ParentRollIds);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Terminal_purpose_snapshot_remains_readable_for_native_receipt_replay_without_a_false_pending_rule(int status)
    {
        Plan(Id(1), status, 0, version: 5, createdBy: null);
        var snapshot = await Source().ReadDecisionAsync(Context(), Id(1), default);
        Assert.NotNull(snapshot); Assert.False(snapshot.Pending); Assert.Equal("5", snapshot.SourceVersion);
        Assert.Null(snapshot.ManufacturingPlanReapproval);
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
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Revision_during_projection_cannot_mix_the_first_source_version_with_a_later_maker(int changedAfterRead)
    {
        Plan(Id(1), 1, 0);
        var racing = new ChangingDecisionRows(rows, read =>
        {
            if (read != changedAfterRead) return;
            rows.PlanRows.Single().Version = 2;
            Revision(Id(1), 1, 2, 10);
        });
        var source = new ManufacturingPlanWorkInboxSourceV1(racing, Fence());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Context(), Id(1), default));
        Assert.Equal("Manufacturing decision changed while reading.", error.Message);
        Assert.Equal(4, racing.Reads); // One bounded final read; no retry or mutation.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_changed_status_or_missing_native_plan_invalidates_the_purpose_projection(bool missing)
    {
        Plan(Id(1), 1, 0);
        var racing = new ChangingDecisionRows(rows, read =>
        {
            if (read != 3) return;
            if (missing) rows.PlanRows.Clear(); else rows.PlanRows.Single().Status = 2;
        });
        var source = new ManufacturingPlanWorkInboxSourceV1(racing, Fence());
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Context(), Id(1), default));
        Assert.Equal(4, racing.Reads);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Fresh_authority_loss_after_any_purpose_query_discloses_no_snapshot(int revokedAt)
    {
        Plan(Id(1), 1, 0);
        authority.Answer = (_, _) => authority.Calls.Count < revokedAt
            ? ManufacturingWorkInboxTestAuthority.CompanyWide : PermissionScope.Denied;
        Assert.Null(await Source().ReadDecisionAsync(Context(), Id(1), default));
    }
    [Fact]
    public async Task Reapproval_options_are_the_complete_current_revision_identity_set_not_the_twelve_reference_cap()
    {
        Plan(Id(1), 2, 0, version: 4);
        Revision(Id(1), 1, 3, 20);
        Order(Id(1), Id(91), 3, planVersionAfterCancel: 4, cancelledMinutes: 50);
        rows.InputRows.Add(new() { CompanyId = CompanyA, PlanId = Id(1), ParentRollId = Id(70) });
        var ids = Enumerable.Range(100, 15).Select(Id).ToArray();
        foreach (var (id, ordinal) in ids.Select((id, ordinal) => (id, ordinal)))
            rows.RevisionInputRows.Add(new() { CompanyId = CompanyA, PlanId = Id(1), RevisionNo = 1, ParentRollId = id, Sequence = ordinal });
        rows.RevisionInputRows.Add(new() { CompanyId = CompanyB, PlanId = Id(1), RevisionNo = 1, ParentRollId = Id(80) });
        rows.RevisionInputRows.Add(new() { CompanyId = CompanyA, PlanId = Id(2), RevisionNo = 1, ParentRollId = Id(81) });
        var snapshot = await Source().ReadDecisionAsync(Context(), Id(1), default);
        Assert.NotNull(snapshot); Assert.Equal(ids, snapshot.ManufacturingPlanReapproval!.ParentRollIds);
        Assert.Single(snapshot.Item.References);
        Assert.True(snapshot.ManufacturingPlanReapproval.ParentRollIds.Count > WorkInboxRulesV1.MaxReferencesPerItem);
        Assert.All(authority.Calls, call => Assert.Equal((ManufacturingPermissionManifestV1.PlansApprove, CompanyA), call));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty-id")]
    [InlineData("duplicate")]
    public async Task Invalid_reapproval_label_projection_never_returns_partial_options(string fault)
    {
        Plan(Id(1), 2, 0, version: 3);
        Order(Id(1), Id(91), 3, planVersionAfterCancel: 3, cancelledMinutes: 50);
        if (fault != "missing") rows.InputRows.Add(new() { CompanyId = CompanyA, PlanId = Id(1), ParentRollId = fault == "empty-id" ? Guid.Empty : Id(70) });
        if (fault == "duplicate") rows.InputRows.Add(new() { CompanyId = CompanyA, PlanId = Id(1), ParentRollId = Id(70) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source().ReadDecisionAsync(Context(), Id(1), default));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Reapproval_option_queries_cannot_mix_a_new_version_with_the_initial_snapshot(int changedAfterRead)
    {
        Plan(Id(1), 2, 0, version: 3);
        Order(Id(1), Id(91), 3, planVersionAfterCancel: 3, cancelledMinutes: 50);
        rows.InputRows.Add(new() { CompanyId = CompanyA, PlanId = Id(1), ParentRollId = Id(70) });
        var racing = new ChangingDecisionRows(rows, read => { if (read == changedAfterRead) rows.PlanRows.Single().Version++; });
        var source = new ManufacturingPlanWorkInboxSourceV1(racing, Fence());
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Context(), Id(1), default));
        Assert.Equal(6, racing.Reads);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Reapproval_options_never_escape_fresh_authority_loss(int revokedAt)
    {
        Plan(Id(1), 2, 0, version: 3);
        Order(Id(1), Id(91), 3, planVersionAfterCancel: 3, cancelledMinutes: 50);
        rows.InputRows.Add(new() { CompanyId = CompanyA, PlanId = Id(1), ParentRollId = Id(70) });
        authority.Answer = (_, _) => authority.Calls.Count < revokedAt ? ManufacturingWorkInboxTestAuthority.CompanyWide : PermissionScope.Denied;
        Assert.Null(await Source().ReadDecisionAsync(Context(), Id(1), default));
    }

    private sealed class ChangingDecisionRows(ManufacturingWorkInboxRows inner, Action<int> afterRead) : ManufacturingWorkInboxRows
    {
        public int Reads { get; private set; }
        public override IQueryable<PlanRow> Plans => inner.Plans;
        public override IQueryable<PlanRevisionRow> PlanRevisions => inner.PlanRevisions;
        public override IQueryable<PlanInputRow> PlanInputs => inner.PlanInputs;
        public override IQueryable<PlanRevisionInputRow> PlanRevisionInputs => inner.PlanRevisionInputs;
        public override IQueryable<PlanRejectionRow> PlanRejections => inner.PlanRejections;
        public override IQueryable<PlanReapprovalRow> PlanReapprovals => inner.PlanReapprovals;
        public override IQueryable<OrderRow> Orders => inner.Orders;
        public override async Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        {
            var result = await inner.ListAsync(query, cancellationToken);
            afterRead(++Reads);
            return result;
        }
    }

}
