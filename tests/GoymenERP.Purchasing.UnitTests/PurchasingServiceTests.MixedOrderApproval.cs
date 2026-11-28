using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    // Controlled Application/store protocol only. A constructed answer here is NOT a genuine SQL receipt proof.
    [Fact]
    public async Task Mixed_application_snapshots_exact_current_intent_before_await_and_releases_only_original_answer()
    {
        var f = MixedPreviewFixture(); var preview = await MixedPreviewService(f.World, f.Preparation).ReadMixedOrderApprovalPreviewAsync(f.Source.Parent.Order.Id, "3");
        var lines = f.Source.Services[0].Lines.Select(x => x.LineId).ToList();
        var selections = new List<PurchaseMixedOrderApprovalSelectionV1> { new(f.Source.Services[0].OrderId, "2", lines) };
        var request = new ApprovePurchaseOrderRequest("3") { MixedServices = selections };
        var json = JsonSerializer.Serialize(request, PurchasingJson.Options); f.World.Store.MixedClean = true;
        f.World.Store.MixedCommand = (frozen, envelope, hash) =>
        {
            lines.Clear(); selections.Clear();
            Assert.Equal(json, JsonSerializer.Serialize(frozen, PurchasingJson.Options));
            Assert.Equal(PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, f.Source.Parent.Order.Id, frozen), hash);
            var answer = ControlledMixedAnswer(f, preview.Parent, frozen, envelope, hash, false);
            var wire = JsonSerializer.Serialize(answer, PurchasingJson.Options);
            Assert.DoesNotContain("receipt", wire, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("children", wire, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mixedServices", wire, StringComparison.OrdinalIgnoreCase);
            return answer;
        };
        var result = await MixedPreviewService(f.World, f.Preparation).ApproveOrderAsync(f.Source.Parent.Order.Id, request, Metadata("mixed-actual-intent"));
        Assert.Equal("4", result.Detail.Order.Version); Assert.False(result.IsReplay); Assert.Equal("Approved", result.Detail.Order.Status);
        Assert.Single(f.Preparation.Reads); // Only the preview above: no second human/preparation read on the release path.
        Assert.DoesNotContain("receipt", JsonSerializer.Serialize(result, PurchasingJson.Options), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mixed_original_key_replay_never_replaces_recorded_answer_with_later_child_or_parent()
    {
        var f = MixedPreviewFixture(); var preview = await MixedPreviewService(f.World, f.Preparation).ReadMixedOrderApprovalPreviewAsync(f.Source.Parent.Order.Id, "3");
        var request = new ApprovePurchaseOrderRequest("3") { MixedServices = [preview.Services[0].Selection] };
        f.World.Store.MixedClean = true;
        f.World.Store.MixedCommand = (frozen, envelope, hash) => ControlledMixedAnswer(f, preview.Parent, frozen, envelope, hash, true);
        f.Preparation.Fault = "version";
        var result = await MixedPreviewService(f.World, f.Preparation).ApproveOrderAsync(f.Source.Parent.Order.Id, request, Metadata("mixed-original-key"));
        Assert.True(result.IsReplay); Assert.Equal("4", result.Detail.Order.Version); Assert.Equal("original-mixed-correlation", result.OriginalCorrelationId);
        Assert.Single(f.Preparation.Reads); Assert.Equal(3L, f.Source.Parent.Order.Version); Assert.Null(f.Source.Parent.Order.Approval);
    }

    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("permission")]
    public async Task Mixed_original_answer_is_not_released_after_current_context_or_authority_changes(string fault)
    {
        var f = MixedPreviewFixture(); var preview = await MixedPreviewService(f.World, f.Preparation).ReadMixedOrderApprovalPreviewAsync(f.Source.Parent.Order.Id, "3");
        f.World.Store.MixedClean = true;
        f.World.Store.MixedCommand = (frozen, envelope, hash) =>
        {
            var answer = ControlledMixedAnswer(f, preview.Parent, frozen, envelope, hash, true);
            if (fault == "actor") f.World.User.CurrentActor = Actor;
            if (fault == "company") f.World.Company.CurrentCompanyId = Guid.NewGuid();
            if (fault == "permission") f.World.Authority.Granted = false;
            return answer;
        };
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedPreviewService(f.World, f.Preparation).ApproveOrderAsync(
            f.Source.Parent.Order.Id, new("3") { MixedServices = [preview.Services[0].Selection] }, Metadata("mixed-context")));
        Assert.Equal(403, failure.Status);
    }

    [Fact]
    public async Task Mixed_unclean_entry_refuses_before_first_authority_and_default_owner_port_is_unavailable()
    {
        var f = MixedPreviewFixture(); var child = f.Source.Services[0]; var before = f.World.Authority.Calls;
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedPreviewService(f.World, f.Preparation)
            .ApproveOrderAsync(f.Source.Parent.Order.Id, new("3") { MixedServices = [new(child.OrderId, "2", child.Lines.Select(x => x.LineId).ToArray())] }, Metadata("mixed-dirty")))).Status);
        Assert.Equal(before, f.World.Authority.Calls);
        IPurchaseMixedOrderApprovalStoreV1 missing = new MissingMixedApproval(); Assert.False(missing.MixedApprovalEntryIsClean);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => missing.ApproveMixedOrderAsync(Guid.NewGuid(), new("1"),
            new(Actor, ApprovePermission, [], "corr", null, "key", "Web"), "hash", true,
            (_, _, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), _ => Task.CompletedTask))).Status);
    }

    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData("-1")] [InlineData("1\n")] [InlineData("9223372036854775807")]
    public void Mixed_intent_requires_incrementable_canonical_versions_and_detaches_all_line_lists(string version)
    {
        Assert.Equal(400, Assert.Throws<PurchasingServiceException>(() => PurchaseMixedOrderApprovalIntentV1.Freeze(new(version)
            { MixedServices = [new(Guid.NewGuid(), "1", [Guid.NewGuid()])] })).Status);
    }

    [Theory]
    [InlineData("missing")] [InlineData("key")] [InlineData("correlation")] [InlineData("version")]
    public async Task Mixed_application_does_not_release_a_missing_or_unbound_owner_receipt(string fault)
    {
        var f = MixedPreviewFixture(); var preview = await MixedPreviewService(f.World, f.Preparation).ReadMixedOrderApprovalPreviewAsync(f.Source.Parent.Order.Id, "3");
        f.World.Store.MixedClean = true;
        f.World.Store.MixedCommand = (request, envelope, hash) =>
        {
            var answer = ControlledMixedAnswer(f, preview.Parent, request, envelope, hash, false);
            return fault switch {
                "missing" => answer with { Receipt = null },
                "key" => answer with { Receipt = answer.Receipt! with { IdempotencyKeyHash = new string('A', 64) } },
                "correlation" => answer with { OriginalCorrelationId = "SENTETIK-other-correlation" },
                _ => answer with { Receipt = answer.Receipt! with { ExpectedVersion = long.MaxValue, ResultVersion = long.MaxValue } },
            };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedPreviewService(f.World, f.Preparation).ApproveOrderAsync(
            f.Source.Parent.Order.Id, new("3") { MixedServices = [preview.Services[0].Selection] }, Metadata("mixed-invalid-owner-proof")))).Status);
    }

    private static PurchaseMixedOrderApprovalStoreAnswerV1 ControlledMixedAnswer(MixedPreviewCase f, PurchaseOrderDetailView detail,
        ApprovePurchaseOrderRequest request, PurchasingWriteEnvelope envelope, string hash, bool replay)
    {
        var header = detail.Order with { Version = "4", Status = "Approved" };
        var original = detail with { Order = header };
        var recorded = new PurchaseMixedOrderRecordedApprovalV1(Company, header.Id, envelope.ActorId, PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey), hash,
            "original-mixed-correlation", f.Source.Parent.Order.UpdatedAtUtc, request, f.Source.Parent.Order.ServiceMemberships, [], original,
            original with { PriceDisclosure = PurchasingPriceDisclosureV1.Withheld, PricePending = null });
        return new(new(Company, header.Id, envelope.ActorId, PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey), hash, "original-mixed-correlation", 3, 4,
            recorded.RecordedAtUtc, Guid.NewGuid(), "controlled-integrity-not-SQL", recorded), null, replay, recorded.OriginalCorrelationId);
    }
    private sealed class MissingMixedApproval : IPurchaseMixedOrderApprovalStoreV1 { }
    private sealed partial class FakeStore : IPurchaseMixedOrderApprovalStoreV1
    {
        internal bool MixedClean;
        internal Func<ApprovePurchaseOrderRequest, PurchasingWriteEnvelope, string, PurchaseMixedOrderApprovalStoreAnswerV1>? MixedCommand;
        public bool MixedApprovalEntryIsClean => MixedClean;
        public async Task<PurchaseMixedOrderApprovalStoreAnswerV1> ApproveMixedOrderAsync(Guid id, ApprovePurchaseOrderRequest request,
            PurchasingWriteEnvelope envelope, string hash, bool clean,
            Func<StoredPurchaseOrder, IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<PurchaseMixedOrderApprovalPreparationV1>> prepare,
            Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> fence, CancellationToken ct = default)
        {
            await fence(ct); var answer = MixedCommand?.Invoke(request, envelope, hash) ?? throw new PurchasingServiceException(503, "service_unavailable");
            await fence(ct); return answer;
        }
    }
}
