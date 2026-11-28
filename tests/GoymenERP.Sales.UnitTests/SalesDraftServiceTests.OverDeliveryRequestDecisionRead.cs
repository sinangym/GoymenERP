using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData("Pending", true)] [InlineData("Approved", false)] [InlineData("Rejected", false)]
    public async Task Over_request_owner_read_keeps_request_version_separate_and_never_requests_general_or_price_read(string state, bool pending)
    {
        var (world, source) = await OverHumanSource(state);
        world.Authority.Denied.Add(ReadPermission); world.Authority.Denied.Add(PriceReadPermission); world.Authority.Measured.Clear();
        var calls = 0; var answer = await world.Service().ReadOverDeliveryRequestDecisionAsync(source.Request.Id, _ => { calls++; return Task.CompletedTask; });
        Assert.Equal(SalesOverDeliveryRequestDecisionReadStateV1.Found, answer.State); var detail = Assert.IsType<SalesOverDeliveryRequestDecisionDetailV1>(answer.Detail);
        Assert.Equal((state, pending, pending), (detail.Request.Status, detail.ApprovalSupported, detail.RejectionSupported));
        Assert.Equal(source.Request.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), detail.Request.Version);
        Assert.Equal(source.Order.Version, detail.Order.Version); Assert.Equal(source.Request.SubjectId, detail.Order.Id);
        Assert.Equal(source.Request.LineId, detail.Order.Line!.Id); Assert.Equal(source.Request.RequestedTotal, detail.Request.RequestedTotal);
        Assert.Equal(5, calls); Assert.Equal(1, world.Store.OverDecisionReads);
        using var hidden = JsonDocument.Parse(JsonSerializer.Serialize(source, SalesDraftJson.Options));
        Assert.Empty(hidden.RootElement.EnumerateObject());
        Assert.DoesNotContain(ReadPermission, world.Authority.Measured); Assert.DoesNotContain(PriceReadPermission, world.Authority.Measured);
        Assert.Equal(SalesOverDeliveryRequestDecisionDigestsV1.SourceFactsDigest(Company, source.Request.Id, detail.Request, detail.Order.DocumentNo), detail.SourceFactsDigest);
        Assert.Equal(SalesOverDeliveryRequestDecisionDigestsV1.PreviewDigest(detail), detail.PreviewDigest);
        Assert.True(SalesOverDeliveryRequestDecisionBindingV1.MatchesDecisionItem(SalesOverDeliveryRequestDecisionBindingV1.Item(detail), detail));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Over_request_owner_each_completed_consumer_rechecks_permission_and_cancellation(int boundary)
    {
        foreach (var cancel in new[] { true, false })
        {
            var (world, source) = await OverHumanSource("Pending"); using var cancellation = new CancellationTokenSource(); var calls = 0;
            Task Fence(CancellationToken _) { if (++calls == boundary) { if (cancel) cancellation.Cancel(); else world.Authority.Denied.Add(OverDeliveryPermission); } return Task.CompletedTask; }
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().ReadOverDeliveryRequestDecisionAsync(source.Request.Id, Fence, cancellation.Token));
            else { var result = await world.Service().ReadOverDeliveryRequestDecisionAsync(source.Request.Id, Fence); Assert.Equal(SalesOverDeliveryRequestDecisionReadStateV1.Denied, result.State); Assert.Null(result.Detail); }
            Assert.Equal(boundary, calls);
        }
    }

    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("wrong-id")] [InlineData("missing")] [InlineData("maker")]
    public async Task Over_request_owner_never_releases_foreign_selection_or_last_consumer_context(string fault)
    {
        var (world, source) = await OverHumanSource("Pending"); var calls = 0;
        if (fault == "missing") world.Store.OverDecisionSource = null;
        if (fault == "maker") world.User.Id = Shipper;
        var result = await world.Service().ReadOverDeliveryRequestDecisionAsync(fault == "wrong-id" ? Guid.NewGuid() : source.Request.Id, _ =>
        { if (++calls == 5) { if (fault == "actor") world.User.Id = "SENTETIK-other"; if (fault == "company") world.User.Company = Guid.NewGuid(); } return Task.CompletedTask; });
        Assert.Equal(fault == "missing" ? SalesOverDeliveryRequestDecisionReadStateV1.NotFound : fault == "wrong-id" ? SalesOverDeliveryRequestDecisionReadStateV1.Unavailable : SalesOverDeliveryRequestDecisionReadStateV1.Denied, result.State);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task Over_request_terminal_read_does_not_apply_a_current_pending_maker_ban_or_relabel_the_original_maker()
    {
        var (world, source) = await OverHumanSource("Approved"); world.User.Id = Shipper;
        var answer = await world.Service().ReadOverDeliveryRequestDecisionAsync(source.Request.Id, _ => Task.CompletedTask);
        Assert.Equal(SalesOverDeliveryRequestDecisionReadStateV1.Found, answer.State); Assert.Equal(Shipper, answer.Detail!.Request.RequestedBy);
        Assert.Equal(Actor, answer.Detail.Request.DecidedBy); Assert.False(answer.Detail.ApprovalSupported);
    }

    [Theory]
    [InlineData("actual")] [InlineData("threshold")] [InlineData("unit")] [InlineData("reference")] [InlineData("caption")] [InlineData("maker")]
    public async Task Over_request_full_owner_matcher_rejects_changed_item_quantity_reference_or_maker(string fault)
    {
        var (world, source) = await OverHumanSource("Pending"); var detail = (await world.Service().ReadOverDeliveryRequestDecisionAsync(source.Request.Id, _ => Task.CompletedTask)).Detail!;
        var item = SalesOverDeliveryRequestDecisionBindingV1.Item(detail)!; var rule = item.Rules[0];
        if (fault == "actual") item = item with { Rules = [rule with { Actual = "999" }] };
        if (fault == "threshold") item = item with { Rules = [rule with { Threshold = "1" }] };
        if (fault == "unit") item = item with { Rules = [rule with { Unit = "kg" }] };
        if (fault == "reference") item = item with { References = [item.References[0] with { RecordId = Guid.NewGuid() }] };
        if (fault == "caption") item = item with { DocumentLabel = "SENTETIK-other" };
        if (fault == "maker") item = item with { PreparedBy = Actor };
        Assert.False(SalesOverDeliveryRequestDecisionBindingV1.MatchesDecisionItem(item, detail));
    }

    [Fact]
    public async Task Over_request_source_current_snapshot_is_quantity_only_and_stays_on_the_native_link()
    {
        var (world, source) = await OverHumanSource("Pending");
        var company = new GoymenERP.BuildingBlocks.Multitenancy.CompanyContext(); company.Set(Company);
        var reader = new SalesOverDeliveryRequestWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(), company, world.User, world.Authority, world.Service());
        var context = new WorkInboxSourceContextV1(Company, Actor, "SENTETIK", null);
        var snapshot = await reader.ReadDecisionAsync(context, source.Request.Id, default); Assert.NotNull(snapshot);
        Assert.Equal("1", snapshot.SourceVersion); Assert.True(snapshot.Pending); Assert.NotNull(snapshot.SalesOverDeliveryRequestProof);
        Assert.Equal($"{SalesOverDeliveryRequestWorkInboxSourceV1.WorkDecisionPath}/{source.Request.Id:D}", snapshot.Item.Link); Assert.Equal(Shipper, snapshot.Item.PreparedBy);
        Assert.True(snapshot.Item.Excludes(Shipper)); Assert.False(snapshot.Item.Excludes(Actor));
        Assert.Null(snapshot.Item.Rules[0].Unit); Assert.Single(snapshot.Item.References);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, SalesDraftJson.Options));
        Assert.False(wire.RootElement.TryGetProperty("salesOverDeliveryRequestProof", out _));
        Assert.False(wire.RootElement.TryGetProperty("sourceFactsDigest", out _));
    }

    [Fact]
    public async Task Over_request_selected_read_initial_cancellation_does_not_read_any_port()
    {
        var world = new World(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().ReadOverDeliveryRequestDecisionAsync(Guid.NewGuid(), _ => Task.CompletedTask, cancellation.Token));
        Assert.Equal(0, world.Store.OverDecisionReads); Assert.Equal(0, world.Authority.Calls);
    }

    private static async Task<(World World, SalesOverDeliveryRequestDecisionSourceV1 Source)> OverHumanSource(string state)
    {
        var world = new World(); var approved = await ApprovedTwoLineAsync(world); var order = (await world.Store.FindDraftAsync(approved.Draft.Id))!;
        var selected = SalesApprovalRequest.RequestOverDelivery(Guid.NewGuid(), Company, order.Id, 1, order.Lines[0].Id, order.Revision,
            1.25m, 0.03m, 1.28m, Guid.NewGuid(), Guid.NewGuid(), Shipper, world.Clock.GetUtcNow());
        if (state == "Approved") selected.Approve(1, Actor, world.Clock.GetUtcNow());
        if (state == "Rejected") selected.Reject(1, Actor, "  SENTETIK ret  ", world.Clock.GetUtcNow());
        var source = new SalesOverDeliveryRequestDecisionSourceV1(selected, order); world.Store.OverDecisionSource = source; return (world, source);
    }
    private sealed partial class FakeStore : ISalesOverDeliveryRequestDecisionReadStoreV1
    {
        internal SalesOverDeliveryRequestDecisionSourceV1? OverDecisionSource; internal int OverDecisionReads;
        internal SalesOverDeliveryRequestOriginalVersionReadResultV1 OverOriginal = new(SalesOverDeliveryRequestDecisionReadStateV1.NotFound, null);
        public async Task<SalesOverDeliveryRequestDecisionSourceV1?> ReadOverDeliveryRequestDecisionSourceAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); OverDecisionReads++; var value = OverDecisionSource; await fence(token); return value; }
        public async Task<SalesOverDeliveryRequestOriginalVersionReadResultV1> ReadOverDeliveryRequestOriginalVersionAsync(Guid id, long version, string actor, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); await fence(token); return OverOriginal; }
    }
}
