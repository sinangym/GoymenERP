using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    // Controlled Application/store protocol; the constructed answer is not an actual SQL receipt.
    [Fact]
    public async Task Membership_uses_native_revise_authority_and_freezes_all_selection_before_the_owner_await()
    {
        var f = MixedPreviewFixture(); f.World.Store.MembershipClean = true;
        f.World.Authority.Denied.Add(ReadPermission); f.World.Authority.Denied.Add(ApprovePermission); f.World.Authority.Denied.Add(CreatePermission);
        var lines = f.Source.Services[0].Lines.Select(x => x.LineId).ToList();
        var services = new List<PurchaseMixedOrderApprovalSelectionV1> { new(f.Source.Services[0].OrderId, "2", lines) };
        var request = new BindPurchaseOrderServicesRequest("2", services);
        var frozenJson = JsonSerializer.Serialize(request, PurchasingJson.Options);
        f.World.Store.MembershipCommand = (body, envelope, hash, project) =>
        {
            services.Clear(); lines.Clear();
            Assert.Equal(frozenJson, JsonSerializer.Serialize(body, PurchasingJson.Options));
            Assert.Equal(RevisePermission, envelope.PermissionCode);
            Assert.Equal(PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, f.Source.Parent.Order.Id, body), hash);
            return ControlledMembershipAnswer(f.Source.Parent, body, envelope, hash, project, false);
        };
        var result = await f.World.Service().BindServicesAsync(f.Source.Parent.Order.Id, request, Metadata("membership-freeze"));
        Assert.Equal("Draft", result.Detail.Order.Status); Assert.Equal("3", result.Detail.Order.Version); Assert.False(result.IsReplay);
        Assert.DoesNotContain(ReadPermission, f.World.Authority.Measured); Assert.DoesNotContain(ApprovePermission, f.World.Authority.Measured);
        Assert.DoesNotContain(CreatePermission, f.World.Authority.Measured);
        Assert.Equal(0, f.World.Store.Replays);
        Assert.DoesNotContain("membership", JsonSerializer.Serialize(result, PurchasingJson.Options), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Membership_unclean_entry_and_missing_default_port_refuse_without_authority_or_mutation()
    {
        var f = MixedPreviewFixture(); var before = f.World.Authority.Calls;
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => f.World.Service().BindServicesAsync(
            f.Source.Parent.Order.Id, MembershipBody(f.Source), Metadata("membership-dirty")))).Status);
        Assert.Equal(before, f.World.Authority.Calls); Assert.Equal(0, f.World.Store.MembershipCalls);
        IPurchaseMixedOrderMembershipStoreV1 missing = new MissingMembership(); Assert.False(missing.MembershipEntryIsClean);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => missing.BindServicesAsync(Guid.NewGuid(),
            MembershipBody(f.Source), new(Actor, RevisePermission, [], "corr", null, "key", "Web"), "hash", true,
            (_, _) => throw new InvalidOperationException(), _ => Task.CompletedTask))).Status);
    }
    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("permission")] [InlineData("cancel")]
    public async Task Membership_original_result_is_not_released_after_context_authority_or_completed_cancellation(string fault)
    {
        var f = MixedPreviewFixture(); f.World.Store.MembershipClean = true; using var cancellation = new CancellationTokenSource();
        f.World.Store.MembershipCommand = (body, envelope, hash, project) =>
        {
            var answer = ControlledMembershipAnswer(f.Source.Parent, body, envelope, hash, project, true);
            if (fault == "actor") f.World.User.CurrentActor = Actor;
            if (fault == "company") f.World.Company.CurrentCompanyId = Guid.NewGuid();
            if (fault == "permission") f.World.Authority.Denied.Add(RevisePermission);
            if (fault == "cancel") cancellation.Cancel();
            return answer;
        };
        if (fault == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.World.Service().BindServicesAsync(
                f.Source.Parent.Order.Id, MembershipBody(f.Source), Metadata("membership-release"), cancellation.Token));
        else
            Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => f.World.Service().BindServicesAsync(
                f.Source.Parent.Order.Id, MembershipBody(f.Source), Metadata("membership-release")))).Status);
        Assert.Equal(1, f.World.Store.MembershipCalls);
    }
    [Fact]
    public async Task Membership_pre_cancel_has_no_owner_or_authority_call()
    {
        var f = MixedPreviewFixture(); f.World.Store.MembershipClean = true; using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var before = f.World.Authority.Calls;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.World.Service().BindServicesAsync(f.Source.Parent.Order.Id,
            MembershipBody(f.Source), Metadata("membership-precancel"), cancellation.Token));
        Assert.Equal(before, f.World.Authority.Calls); Assert.Equal(0, f.World.Store.MembershipCalls);
    }
    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData("9223372036854775807")] [InlineData("1\n")]
    public void Membership_expected_versions_are_canonical_and_incrementable(string version)
    {
        var request = new BindPurchaseOrderServicesRequest(version, [new(Guid.NewGuid(), "1", [Guid.NewGuid()])]);
        Assert.Equal(400, Assert.Throws<PurchasingServiceException>(() => PurchaseMixedOrderMembershipV1.Freeze(request)).Status);
    }
    [Fact]
    public void Membership_frozen_body_hash_binds_parent_child_versions_and_complete_line_selection()
    {
        var id = Guid.NewGuid(); var line = Guid.NewGuid(); var child = Guid.NewGuid();
        var body = new BindPurchaseOrderServicesRequest("2", [new(child, "1", [line])]);
        var hash = PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, id, body);
        Assert.NotEqual(hash, PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, id, body with { ExpectedVersion = "3" }));
        Assert.NotEqual(hash, PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, id, body with { Services = [new(child, "2", [line])] }));
        Assert.NotEqual(hash, PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, id, body with { Services = [new(child, "1", [Guid.NewGuid()])] }));
        Assert.Equal(400, Assert.Throws<PurchasingServiceException>(() => PurchaseMixedOrderMembershipV1.Freeze(body with { Services = [body.Services[0], body.Services[0]] })).Status);
    }
    [Fact]
    public void Membership_reads_the_full_native_child_version_without_pretending_to_increment_or_approve_it()
    {
        var selected = new BindPurchaseOrderServicesRequest("2", [new(Guid.NewGuid(), "9223372036854775807", [Guid.NewGuid()])]);
        var frozen = PurchaseMixedOrderMembershipV1.Freeze(selected);
        Assert.Equal(long.MaxValue, PurchaseMixedOrderMembershipV1.SelectedChildVersion(frozen.Services[0].ExpectedVersion));
        Assert.Equal("9223372036854775807", frozen.Services[0].ExpectedVersion);
        Assert.Equal(400, Assert.Throws<PurchasingServiceException>(() => PurchaseMixedOrderMembershipV1.SelectedChildVersion("01")).Status);
    }
    [Theory]
    [InlineData("key")] [InlineData("actor")] [InlineData("version")]
    public async Task Membership_does_not_release_an_unbound_original_answer(string fault)
    {
        var f = MixedPreviewFixture(); f.World.Store.MembershipClean = true;
        f.World.Store.MembershipCommand = (body, envelope, hash, project) =>
        {
            var result = ControlledMembershipAnswer(f.Source.Parent, body, envelope, hash, project, true);
            return result with { Receipt = fault switch {
                "key" => result.Receipt with { KeyHash = "different" },
                "actor" => result.Receipt with { ActorId = "different" },
                _ => result.Receipt with { FrozenRequest = body with { ExpectedVersion = "1" } } } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => f.World.Service().BindServicesAsync(
            f.Source.Parent.Order.Id, MembershipBody(f.Source), Metadata("membership-invalid-answer")))).Status);
    }
    private static BindPurchaseOrderServicesRequest MembershipBody(PurchaseMixedOrderPreparedPreviewV1 source) => new("2",
        [new(source.Services[0].OrderId, "2", source.Services[0].Lines.Select(x => x.LineId).ToArray())]);
    private static PurchaseMixedOrderMembershipAnswerV1 ControlledMembershipAnswer(StoredPurchaseOrder parent,
        BindPurchaseOrderServicesRequest body, PurchasingWriteEnvelope envelope, string hash,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, bool replay) => new(new(parent.Order.CompanyId,
            parent.Order.Id, envelope.ActorId, PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey), hash,
            envelope.CorrelationId, parent.Order.UpdatedAtUtc, body, project(parent, PurchasingPriceDisclosureV1.Visible),
            project(parent, PurchasingPriceDisclosureV1.Withheld)), replay);
    private sealed class MissingMembership : IPurchaseMixedOrderMembershipStoreV1 { }
    private sealed partial class FakeStore : IPurchaseMixedOrderMembershipStoreV1
    {
        internal bool MembershipClean; internal int MembershipCalls;
        internal Func<BindPurchaseOrderServicesRequest, PurchasingWriteEnvelope, string,
            Func<StoredPurchaseOrder, string, PurchaseOrderDetailView>, PurchaseMixedOrderMembershipAnswerV1>? MembershipCommand;
        public bool MembershipEntryIsClean => MembershipClean;
        public async Task<PurchaseMixedOrderMembershipAnswerV1> BindServicesAsync(Guid id, BindPurchaseOrderServicesRequest request,
            PurchasingWriteEnvelope envelope, string hash, bool clean, Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project,
            Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            MembershipCalls++; Assert.True(clean); await fence(token);
            var result = MembershipCommand?.Invoke(request, envelope, hash, project) ?? throw new PurchasingServiceException(503, "service_unavailable");
            await fence(token); return result;
        }
    }
}
