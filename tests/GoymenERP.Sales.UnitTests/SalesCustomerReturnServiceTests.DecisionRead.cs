using System.Text.Json;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using static GoymenERP.Sales.UnitTests.SalesCustomerReturnFixtures;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesCustomerReturnServiceTests
{
    [Fact]
    public async Task Return_purpose_reuses_full_native_human_view_without_a_general_returns_read_grant()
    {
        var f = SafetyWorld(); var answer = await f.Service.ReadCustomerReturnDecisionAsync(ReturnId, _ => Task.CompletedTask);
        Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Found, answer.State); var d = Assert.IsType<SalesCustomerReturnDecisionDetailV1>(answer.Detail);
        Assert.Equal((Company, ReturnId, "2", true, true), (d.CompanyId, d.CustomerReturn.Id, d.CustomerReturn.Version, d.ApprovalSupported, d.RejectionSupported));
        Assert.Equal(SalesCustomerReturnDecisionDigestsV1.SourceFactsDigest(Company, d.CustomerReturn), d.SourceFactsDigest);
        Assert.Equal(SalesCustomerReturnDecisionDigestsV1.PreviewDigest(d), d.PreviewDigest); Assert.Equal(0, f.Invoices.Calls);
        Assert.DoesNotContain(SalesPermissionManifestV1.ReturnsRead, f.Authority.Asked);
        Assert.Equal(JsonSerializer.Serialize(SalesCustomerReturnService.View(f.Store.Stored!), SalesDraftJson.Options), JsonSerializer.Serialize(SalesCustomerReturnService.ProjectDecisionReturn(f.Store.Stored!), SalesDraftJson.Options));
        var wire = JsonSerializer.SerializeToElement(d.CustomerReturn, SalesDraftJson.Options);
        Assert.Equal(34, wire.EnumerateObject().Count()); Assert.Equal(14, wire.GetProperty("lines")[0].EnumerateObject().Count());
        Assert.Equal(6, wire.GetProperty("totals").EnumerateObject().Count());
        Assert.Throws<NotSupportedException>(() => ((IList<SalesCustomerReturnLineView>)d.CustomerReturn.Lines)[0] = d.CustomerReturn.Lines[0]);
    }
    [Theory] [InlineData("approve")] [InlineData("price")] [InlineData("actor")] [InlineData("company")] [InlineData("member")] [InlineData("cancel")]
    public async Task Return_last_completed_consumer_cannot_release_after_current_authority_context_or_cancellation_loss(string fault)
    {
        var f = SafetyWorld(); using var cts = new CancellationTokenSource(); var reads = 0;
        Task<SalesCustomerReturnDecisionReadResultV1> Read() => f.Service.ReadCustomerReturnDecisionAsync(ReturnId, _ =>
        {
            if (++reads == 5) { if (fault == "approve") f.Authority.Denied.Add(SalesPermissionManifestV1.ReturnsApprove);
                if (fault == "price") f.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead); if (fault == "actor") f.User.Id = "SENTETIK-other";
                if (fault == "company") f.Company.Id = Guid.NewGuid(); if (fault == "member") f.User.Member = false; if (fault == "cancel") cts.Cancel(); }
            return Task.CompletedTask;
        }, cts.Token);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Read);
        else { var answer = await Read(); Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Denied, answer.State); Assert.Null(answer.Detail); }
        Assert.Equal(5, reads); Assert.Equal(2L, f.Store.Stored!.Version);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Return_owner_denies_both_actual_makers_without_exposing_the_human_view(bool preparer)
    {
        var f = SafetyWorld(preparer ? Preparer : "SENTETIK-submitter");
        var answer = await f.Service.ReadCustomerReturnDecisionAsync(ReturnId, _ => Task.CompletedTask);
        Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Denied, answer.State); Assert.Null(answer.Detail);
    }
    [Fact]
    public async Task Return_owner_captures_domain_and_lines_before_the_final_consumer_can_change_the_provider_object()
    {
        var f = SafetyWorld(); var reads = 0;
        var answer = await f.Service.ReadCustomerReturnDecisionAsync(ReturnId, _ =>
        { if (++reads == 4) f.Store.Stored!.Approve(2, Approver, Now.AddMinutes(1)); return Task.CompletedTask; });
        var d = Assert.IsType<SalesCustomerReturnDecisionDetailV1>(answer.Detail);
        Assert.Equal(("PendingApproval", "2"), (d.CustomerReturn.State, d.CustomerReturn.Version)); Assert.Equal(3L, f.Store.Stored!.Version);
        Assert.Equal(SalesCustomerReturnDecisionDigestsV1.PreviewDigest(d), d.PreviewDigest);
    }
    [Theory] [InlineData("0")] [InlineData("02")] [InlineData("9223372036854775807")]
    public async Task Return_historical_selection_is_canonical_and_incrementable_before_store_IO(string version)
    {
        var f = SafetyWorld(); var answer = await f.Service.ReadCustomerReturnOriginalVersionAsync(ReturnId, version, _ => Task.CompletedTask);
        Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Unavailable, answer.State); Assert.Null(answer.Receipt); Assert.Equal(0, f.Store.DecisionOriginalReads);
    }
    [Fact]
    public async Task Return_synthetic_native_success_does_not_invent_a_genuine_decision_receipt()
    {
        var f = SafetyWorld(); var answer = await DecideSafe(f, new("2"), true); Assert.Null(answer.OriginalDecisionReceipt);
        Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Unavailable, (await f.Service.ReadCustomerReturnOriginalVersionAsync(ReturnId, "2", _ => Task.CompletedTask)).State);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Return_historical_last_completed_consumer_revocation_or_cancel_never_releases_the_original_receipt(bool cancel)
    {
        var artifact = await ReturnReceiptFixture(true); var receipt = Assert.IsType<SalesCustomerReturnOriginalReceiptV1>(RestoreReturn(artifact)); var f = SafetyWorld();
        f.Store.DecisionOriginalAnswer = new(SalesCustomerReturnDecisionReadStateV1.Found, receipt) { CurrentVersion = 3 };
        using var cts = new CancellationTokenSource(); var callbacks = 0;
        Task<SalesCustomerReturnOriginalReadResultV1> Read() => f.Service.ReadCustomerReturnOriginalVersionAsync(ReturnId, "2", _ =>
        { if (++callbacks == 4) { if (cancel) cts.Cancel(); else f.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead); } return Task.CompletedTask; }, cts.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Read);
        else { var answer = await Read(); Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Denied, answer.State); Assert.Null(answer.Receipt); }
        Assert.Equal(4, callbacks);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Return_historical_provider_cannot_certify_missing_original_human_or_changed_immutable_digest(bool missing)
    {
        var artifact = await ReturnReceiptFixture(true); var r = Assert.IsType<SalesCustomerReturnOriginalReceiptV1>(RestoreReturn(artifact));
        r = missing ? r with { OriginalDecision = null! } : r with { SourceFactsDigest = new('a', 64) };
        r = r with { OriginalDecisionDigest = SalesCustomerReturnDecisionDigestsV1.OriginalDecisionDigest(r) };
        var f = SafetyWorld(); f.Store.DecisionOriginalAnswer = new(SalesCustomerReturnDecisionReadStateV1.Found, r) { CurrentVersion = 3 };
        var answer = await f.Service.ReadCustomerReturnOriginalVersionAsync(ReturnId, "2", _ => Task.CompletedTask);
        Assert.Equal(SalesCustomerReturnDecisionReadStateV1.Unavailable, answer.State); Assert.Null(answer.Receipt);
    }
    private sealed partial class Store : ISalesCustomerReturnDecisionReadStoreV1
    {
        internal int DecisionOriginalReads; internal SalesCustomerReturnOriginalReadResultV1? DecisionOriginalAnswer;
        public async Task<SalesCustomerReturn?> ReadCustomerReturnDecisionSourceAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); var item = Stored?.Id == id ? Stored : null; await fence(token); return item; }
        public async Task<SalesCustomerReturnOriginalReadResultV1> ReadCustomerReturnOriginalVersionAsync(Guid id, long expectedVersion, string actorId, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { DecisionOriginalReads++; await fence(token); await fence(token); return DecisionOriginalAnswer ?? new(SalesCustomerReturnDecisionReadStateV1.Unavailable, null); }
        public Task<SalesCustomerReturnCommandResult> AttachCustomerReturnOriginalReceiptAsync(SalesCustomerReturnCommandResult response, Guid id, SalesDraftStateCommandRequest request, CancellationToken token = default) => Task.FromResult(response);
    }
}
