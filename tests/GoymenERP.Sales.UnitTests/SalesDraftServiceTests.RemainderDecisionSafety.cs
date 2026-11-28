using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Remainder_guard_composition_is_monotonic_and_missing_boundary_never_reaches_native_IO(bool approve)
    {
        var w = new World(); var service = w.Service(); Assert.Same(service, service.WithRemainderDecisionSafety()); Assert.Same(service, service.WithRemainderDecisionSafety());
        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => approve
            ? service.ApproveRemainderChangeAsync(Guid.NewGuid(), 1, new("1"), Metadata("SENTETIK-remainder"))
            : service.RejectRemainderChangeAsync(Guid.NewGuid(), 1, new("1", "SENTETIK"), Metadata("SENTETIK-remainder")));
        Assert.Equal((503, "service_unavailable"), (error.Status, error.Code)); Assert.Equal(0, w.Store.RemainderProtocolCalls);
        Assert.Empty(w.Store.Transitions); Assert.Empty(w.Store.Refusals); Assert.Equal(0, w.Store.Attempts);
    }

    [Theory] [InlineData("unauthenticated", 401)] [InlineData("denied", 403)] [InlineData("scoped", 403)] [InlineData("membership", 403)]
    public async Task Remainder_guard_keeps_native_authority_before_entry_refusal(string fault, int status)
    {
        var w = new World(); if (fault == "unauthenticated") w.User.Authenticated = false; if (fault == "denied") w.Authority.Granted = false;
        if (fault == "scoped") w.Authority.CompanyWide = false; if (fault == "membership") w.User.Company = Guid.NewGuid();
        var e = await Assert.ThrowsAsync<SalesDraftServiceException>(() => w.Service().WithRemainderDecisionSafety().ApproveRemainderChangeAsync(Guid.NewGuid(), 1, new("1"), Metadata("SENTETIK-authority")));
        Assert.Equal(status, e.Status); Assert.Empty(w.Store.Transitions); Assert.Empty(w.Store.Refusals); Assert.Equal(0, w.Store.RemainderProtocolCalls);
    }

    [Theory] [InlineData("0")] [InlineData("1.0")] [InlineData("9223372036854775808")]
    public async Task Remainder_guard_keeps_authorized_raw_version_shape_refusal_even_with_an_unsafe_caller(string version)
    {
        var w = new World(); w.Store.RemainderSyntheticProtocol = true; w.Store.RemainderClean = false;
        var e = await Assert.ThrowsAsync<SalesDraftServiceException>(() => w.Service().WithRemainderDecisionSafety().RejectRemainderChangeAsync(Guid.NewGuid(), 1, new(version), Metadata("SENTETIK-shape")));
        Assert.Equal((400, "validation_failed"), (e.Status, e.Code)); Assert.Equal("expectedVersion", Assert.Single(e.Errors).Field); Assert.Equal(0, w.Store.RemainderProtocolCalls);
    }

    [Fact]
    public async Task Remainder_guard_keeps_native_approval_reason_shape_before_unsafe_entry_and_initial_cancellation_before_authority()
    {
        var w = new World(); var service = w.Service().WithRemainderDecisionSafety();
        var e = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.ApproveRemainderChangeAsync(Guid.NewGuid(), 1, new("01", "SENTETIK"), Metadata("SENTETIK-reason")));
        Assert.Equal((400, "reason"), (e.Status, Assert.Single(e.Errors).Field)); var reads = w.Authority.Calls;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RejectRemainderChangeAsync(Guid.NewGuid(), 1, new("1"), Metadata("SENTETIK-cancel"), cancellation.Token));
        Assert.Equal(reads, w.Authority.Calls); Assert.Equal(0, w.Store.RemainderProtocolCalls);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Remainder_guard_remembers_initial_unsafe_entry_and_rechecks_state_created_during_authority(bool initialUnsafe)
    {
        var w = new World(); w.Store.RemainderSyntheticProtocol = true; w.Store.RemainderClean = !initialUnsafe;
        w.Authority.OnRead = (_, _) => w.Store.RemainderClean = initialUnsafe;
        var e = await Assert.ThrowsAsync<SalesDraftServiceException>(() => w.Service().WithRemainderDecisionSafety().RejectRemainderChangeAsync(Guid.NewGuid(), 1, new("1"), Metadata("SENTETIK-entry")));
        Assert.Equal(503, e.Status); Assert.Equal(initialUnsafe, w.Store.RemainderClean); Assert.Equal(0, w.Store.RemainderProtocolCalls); Assert.Empty(w.Store.Refusals);
    }

    [Theory] [InlineData(true, true)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(false, false)]
    public async Task Remainder_guard_preserves_only_the_native_SalesSupport_persona_self_restriction(bool approve, bool support)
    {
        var (w, order) = await RemainderProtocolWorld(); if (support) w.ActiveRoleCodes.Codes.Add(ErpPersonaRoleCodes.SalesSupport);
        var service = w.Service().WithRemainderDecisionSafety(); var body = new SalesDraftRemainderDecisionRequest("06", approve ? null : "  SENTETIK ret  ");
        var before = w.Store.Transitions.Count; Task<SalesOrderDraftResponse> Decide() => approve
            ? service.ApproveRemainderChangeAsync(order.Id, 1, body, Metadata("SENTETIK-persona"))
            : service.RejectRemainderChangeAsync(order.Id, 1, body, Metadata("SENTETIK-persona"));
        if (support) { Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(Decide)).Status); Assert.Equal(6, order.Version); Assert.Equal(before, w.Store.Transitions.Count); }
        else
        {
            var first = await Decide(); var replay = await Decide(); Assert.Equal("7", first.Detail.Draft.Version); Assert.True(replay.IsReplay);
            Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(before + 1, w.Store.Transitions.Count);
            var decision = Assert.Single(order.RemainderChanges).Decision!; Assert.Equal(Actor, decision.DecidedBy); Assert.Equal(approve ? null : "SENTETIK ret", decision.Reason);
        }
        Assert.Equal(approve && !support ? 1 : 0, ((SyntheticRemainderLoading)w.Loadings!).Reads);
    }

    [Fact]
    public async Task Remainder_final_optional_price_permission_only_reduces_the_native_answer()
    {
        var (w, order) = await RemainderProtocolWorld(); w.Authority.Denied.Add(w.Bindings.PriceReadPermission!);
        var answer = await w.Service().WithRemainderDecisionSafety().RejectRemainderChangeAsync(order.Id, 1, new("6"), Metadata("SENTETIK-withheld"));
        Assert.All(answer.Detail.Lines, line => { Assert.Equal(SalesPriceDisclosure.Withheld, line.PriceDisclosure); Assert.Null(line.UnitPrice); Assert.Null(line.PriceUnitId); Assert.Null(line.DiscountPercent); });
        Assert.Equal("7", answer.Detail.Draft.Version);
    }

    [Theory] [InlineData("actor")] [InlineData("company")] [InlineData("cancel")]
    public async Task Remainder_final_completed_price_read_cannot_release_nonprice_data_after_context_drift_or_cancellation(string fault)
    {
        var (w, order) = await RemainderProtocolWorld(); var priceReads = 0; using var cts = new CancellationTokenSource();
        w.Authority.OnRead = (permission, _) =>
        {
            if (permission == w.Bindings.PriceReadPermission && ++priceReads == 3)
            { if (fault == "actor") w.User.Id = "SENTETIK-other"; else if (fault == "company") w.User.Company = Guid.NewGuid(); else cts.Cancel(); }
        };
        Task<SalesOrderDraftResponse> Decide() => w.Service().WithRemainderDecisionSafety().RejectRemainderChangeAsync(order.Id, 1, new("6"), Metadata("SENTETIK-final-release"), cts.Token);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Decide);
        else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(Decide)).Status);
        Assert.Equal(3, priceReads); Assert.Equal(7, order.Version); // Synthetic native decision happened; release denial is not rollback evidence.
    }

    [Fact]
    public async Task Rejection_remains_independent_of_missing_loading_and_allocation_owners()
    {
        var (w, order) = await RemainderProtocolWorld(); w.Loadings = null; w.Allocations = null;
        var answer = await w.Service().WithRemainderDecisionSafety().RejectRemainderChangeAsync(order.Id, 1, new("6", null), Metadata("SENTETIK-independent-reject"));
        Assert.Equal("7", answer.Detail.Draft.Version); Assert.Equal("1.25", order.Lines[0].QuantityText); Assert.Null(Assert.Single(order.RemainderChanges).Decision!.Reason);
    }

    private static async Task<(World World, SalesOrderDraft Order)> RemainderProtocolWorld()
    {
        var w = new World(); var approved = await ApprovedTwoLineAsync(w); var order = (await w.Store.FindDraftAsync(approved.Draft.Id))!;
        order.RequestRemainderChange(order.Version, order.Lines[0].Id, RemainderChangeKind.Revise, "1.5", 1.5m, "SENTETIK kalan", new(0.8m, 0.6m, 0.5m), Actor, w.Clock.GetUtcNow());
        w.Loadings = new SyntheticRemainderLoading(order); w.Store.RemainderSyntheticProtocol = true; return (w, order);
    }
    private sealed class SyntheticRemainderLoading(SalesOrderDraft order) : ISalesOrderLoadingReadPortV1
    {
        internal int Reads;
        public Task<SalesOrderLoadingReadAnswerV1> ReadOrderLoadingAsync(Guid id, CancellationToken ct = default)
        { Reads++; return Task.FromResult(new SalesOrderLoadingReadAnswerV1(Company, id, [new(order.Lines[0].Id, KgUnit, 1, 1m, 0.8m, 0.6m, 0.5m)])); }
    }
    // Explicit synthetic protocol port: core policy/dispatch evidence only, never actual ownership/rollback/SQL evidence.
    private sealed partial class FakeStore : ISalesRemainderDecisionSafetyStoreV1
    {
        internal bool RemainderSyntheticProtocol, RemainderClean = true; internal int RemainderProtocolCalls;
        public bool RemainderDecisionEntryIsClean => RemainderSyntheticProtocol && RemainderClean;
        public async Task<SalesOrderDraftResponse> RunRemainderDecisionAsync(SalesDraftWriteEnvelope envelope, string hash, string command,
            bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesOrderDraftResponse>> execute,
            Func<CancellationToken, Task<SalesOrderDraftResponse?>> readOriginalKey, Func<CancellationToken, Task> fence, CancellationToken ct)
        { RemainderProtocolCalls++; Assert.True(cleanBeforeAuthority); Assert.True(RemainderDecisionEntryIsClean); await fence(ct); var answer = await execute(ct); await fence(ct); return answer; }
    }
}
