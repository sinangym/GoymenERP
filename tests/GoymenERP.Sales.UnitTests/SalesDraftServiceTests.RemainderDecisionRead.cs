using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Remainder_purpose_is_quantity_only_and_reuses_the_complete_native_recorded_change()
    {
        var (w, order) = await RemainderProtocolWorld(); var service = w.Service().WithRemainderDecisionSafety();
        var answer = await service.ReadRemainderDecisionAsync(order.Id, _ => Task.CompletedTask);
        Assert.Equal(SalesRemainderDecisionReadStateV1.Found, answer.State); var d = Assert.IsType<SalesRemainderDecisionDetailV1>(answer.Detail);
        Assert.Equal((Company, order.Id, "6", 1, true, true), (d.CompanyId, d.DraftId, d.Version, d.Change.ChangeNo, d.ApprovalSupported, d.RejectionSupported));
        Assert.Equal("1.5", d.Change.RequestedQuantity); Assert.Equal("1.25", d.Line!.CurrentQuantity); Assert.Equal(Actor, d.DraftCreatedBy); Assert.False(d.IsSalesSupport);
        Assert.Equal(SalesRemainderDecisionDigestsV1.SourceFactsDigest(Company, order.Id, order.DocumentNo, d.Change), d.SourceFactsDigest);
        Assert.Equal(SalesRemainderDecisionDigestsV1.PreviewDigest(d), d.PreviewDigest);
        var json = JsonSerializer.SerializeToElement(d, SalesDraftJson.Options);
        Assert.Equal(19, json.GetProperty("change").EnumerateObject().Count()); Assert.False(json.TryGetProperty("draftCreatedBy", out _)); Assert.False(json.TryGetProperty("isSalesSupport", out _));
        Assert.DoesNotContain("unitPrice", json.GetRawText(), StringComparison.Ordinal); Assert.DoesNotContain("discount", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Remainder_purpose_does_not_turn_the_native_SalesSupport_persona_rule_into_a_global_maker_ban(bool support)
    {
        var (w, order) = await RemainderProtocolWorld(); if (support) w.ActiveRoleCodes.Codes.Add(ErpPersonaRoleCodes.SalesSupport);
        var result = await w.Service().WithRemainderDecisionSafety().ReadRemainderDecisionAsync(order.Id, _ => Task.CompletedTask);
        Assert.Equal(support ? SalesRemainderDecisionReadStateV1.Denied : SalesRemainderDecisionReadStateV1.Found, result.State);
        if (support) Assert.Null(result.Detail); Assert.Equal(6, order.Version);
    }

    [Theory] [InlineData("authority")] [InlineData("actor")] [InlineData("company")] [InlineData("persona")]
    public async Task Remainder_owner_final_completed_consumer_cannot_release_after_permission_context_or_persona_loss(string fault)
    {
        var (w, order) = await RemainderProtocolWorld(); var callbacks = 0;
        var result = await w.Service().WithRemainderDecisionSafety().ReadRemainderDecisionAsync(order.Id, _ =>
        {
            // Entry consumer, two synthetic store consumers, after-store consumer, final materialized-detail consumer.
            if (++callbacks == 5) { if (fault == "authority") w.Authority.Granted = false; else if (fault == "actor") w.User.Id = "SENTETIK-other";
                else if (fault == "company") w.User.Company = Guid.NewGuid(); else w.ActiveRoleCodes.Codes.Add(ErpPersonaRoleCodes.SalesSupport); }
            return Task.CompletedTask;
        });
        Assert.Equal(5, callbacks); Assert.Equal(SalesRemainderDecisionReadStateV1.Denied, result.State); Assert.Null(result.Detail); Assert.Equal(6, order.Version);
    }

    [Fact]
    public async Task Remainder_pending_list_does_not_release_exclusions_bound_to_an_old_persona()
    {
        var (w, _) = await RemainderProtocolWorld(); var callbacks = 0; var fired = false;
        var result = await w.Service().WithRemainderDecisionSafety().ListPendingRemainderDecisionsAsync(10, _ =>
        { if (++callbacks == 4) { fired = true; w.ActiveRoleCodes.Codes.Add(ErpPersonaRoleCodes.SalesSupport); } return Task.CompletedTask; });
        Assert.True(fired); Assert.Equal(SalesRemainderDecisionReadStateV1.Unavailable, result.State); Assert.Null(result.Items); Assert.False(result.HasMore); Assert.Null(result.IsSalesSupport);
    }

    [Fact]
    public async Task Remainder_owner_honors_completed_final_consumer_cancellation_and_initial_cancellation()
    {
        var (w, order) = await RemainderProtocolWorld(); using var cts = new CancellationTokenSource(); var callbacks = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => w.Service().WithRemainderDecisionSafety().ReadRemainderDecisionAsync(order.Id, _ =>
        { if (++callbacks == 5) cts.Cancel(); return Task.CompletedTask; }, cts.Token)); Assert.Equal(5, callbacks);
        var reads = w.Store.RemainderOwnerReads;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => w.Service().WithRemainderDecisionSafety().ReadRemainderDecisionAsync(order.Id, _ => throw new InvalidOperationException(), cts.Token)); Assert.Equal(reads, w.Store.RemainderOwnerReads);
    }

    [Theory] [InlineData("0")] [InlineData("01")] [InlineData("9223372036854775807")]
    public async Task Historical_remainder_selection_rejects_noncanonical_or_nonincrementable_versions_without_reading_source(string version)
    {
        var (w, order) = await RemainderProtocolWorld();
        var result = await w.Service().WithRemainderDecisionSafety().ReadRemainderOriginalVersionAsync(order.Id, version, _ => Task.CompletedTask);
        Assert.Equal(SalesRemainderDecisionReadStateV1.Unavailable, result.State); Assert.Null(result.Receipt); Assert.Equal(0, w.Store.RemainderOriginalReads);
    }

    [Fact]
    public async Task Synthetic_owner_without_genuine_artifacts_never_certifies_a_native_decision_receipt()
    {
        var (w, order) = await RemainderProtocolWorld(); var service = w.Service().WithRemainderDecisionSafety();
        var answer = await service.RejectRemainderChangeAsync(order.Id, 1, new("6"), Metadata("SENTETIK-native-no-proof"));
        Assert.Equal("7", answer.Detail.Draft.Version); Assert.Null(answer.OriginalRemainderDecisionReceipt);
        var old = await service.ReadRemainderOriginalVersionAsync(order.Id, "6", _ => Task.CompletedTask);
        Assert.Equal(SalesRemainderDecisionReadStateV1.Unavailable, old.State); Assert.Null(old.Receipt);
    }

    // Protocol-only owner seam: no SQL, Audit provenance or transaction success is invented by this fixture.
    private sealed partial class FakeStore : ISalesRemainderDecisionReadStoreV1
    {
        internal int RemainderOwnerReads, RemainderOriginalReads;
        public async Task<IReadOnlyList<SalesRemainderPendingV1>> ListPendingRemainderDecisionSourceAsync(int take, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            await fence(token); var rows = _drafts.Where(x => x.PendingRemainderChange is not null).Select(x =>
            { var r = x.PendingRemainderChange!.Request; return new SalesRemainderPendingV1(x.CompanyId, x.Id, x.DocumentNo, x.CreatedBy, x.Version, r.ChangeNo, r.Revision, r.LineId, r.RequestedBy, r.RequestedAtUtc); })
                .OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.DraftId).Take(take).ToArray(); await fence(token); return rows;
        }
        public async Task<SalesRemainderDecisionSourceV1?> ReadRemainderDecisionSourceAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { RemainderOwnerReads++; await fence(token); var order = await FindDraftAsync(id, token); var change = order?.RemainderChanges.OrderByDescending(x => x.Request.ChangeNo).FirstOrDefault(); await fence(token); return order is null || change is null ? null : new(order, change); }
        public async Task<SalesRemainderOriginalVersionReadResultV1> ReadRemainderOriginalVersionAsync(Guid id, long version, string actor, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { RemainderOriginalReads++; await fence(token); return new(SalesRemainderDecisionReadStateV1.Unavailable, null); }
        public Task<SalesOrderDraftResponse> AttachRemainderOriginalReceiptAsync(SalesOrderDraftResponse response, Guid id, int changeNo, SalesDraftRemainderDecisionRequest request, CancellationToken token = default) => Task.FromResult(response);
    }
}
