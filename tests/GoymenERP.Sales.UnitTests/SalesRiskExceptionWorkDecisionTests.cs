using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesRiskExceptionWorkDecisionTests
{
    [Theory]
    [InlineData("Draft", true)] [InlineData("Closed", false)]
    public async Task Selected_source_preserves_actual_pending_and_independent_reject_action(string orderState, bool approve)
    {
        var detail = Detail(orderState); var owner = new Owner(detail); var authority = new Authority(p => p == SalesPermissionManifestV1.OrdersRiskExceptionApprove);
        var source = Source(owner, authority); var snapshot = await source.ReadDecisionAsync(Context(), detail.ExceptionId, default);
        Assert.NotNull(snapshot); Assert.True(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion);
        Assert.NotNull(snapshot.SalesRiskExceptionProof); Assert.Equal((approve, true), (snapshot.SalesRiskExceptionProof.ApprovalSupported, snapshot.SalesRiskExceptionProof.RejectionSupported));
        Assert.True(SalesRiskExceptionDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail));
        Assert.Equal($"/satis/siparisler/{detail.Exception.OrderId:D}", snapshot.Item.Link);
        Assert.Equal(detail.Exception.OrderId, Assert.Single(snapshot.Item.References).RecordId);
        Assert.All(authority.Asked, p => Assert.Equal(SalesPermissionManifestV1.OrdersRiskExceptionApprove, p));
        Assert.Equal(1, owner.Reads); Assert.Equal(0, owner.HistoryReads);
    }
    [Fact]
    public async Task Terminal_selected_read_does_not_turn_a_recorded_decision_into_a_pending_action()
    {
        var detail = Detail(); var s = detail.Source!; var at = s.Recorded.RequestedAtUtc.AddHours(1);
        var terminal = s with { Recorded = s.Recorded with { State = "Approved", Version = 2, DecidedBy = Approver, DecidedAtUtc = at, ValidOn = new(2026, 11, 20) } };
        var current = SalesRiskExceptionService.ProjectRiskDecision(terminal, new(2026, 11, 21)); var owner = new Owner(current);
        var snapshot = await Source(owner).ReadDecisionAsync(Context(), current.ExceptionId, default);
        Assert.NotNull(snapshot); Assert.False(snapshot.Pending); Assert.Equal("2", snapshot.SourceVersion);
        Assert.False(snapshot.SalesRiskExceptionProof!.ApprovalSupported); Assert.False(snapshot.SalesRiskExceptionProof.RejectionSupported);
        Assert.False(current.Exception.IsValidToday); Assert.Equal(0, owner.HistoryReads);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source(owner).ReadDecisionVersionAsync(Context(), current.ExceptionId, "1", default));
        Assert.Equal(1, owner.HistoryReads); // No current-row fallback for a missing original receipt.
    }
    [Theory]
    [InlineData("company")] [InlineData("id")] [InlineData("digest")] [InlineData("action")] [InlineData("null-exception")]
    public async Task Malformed_selected_owner_facts_are_not_admitted(string fault)
    {
        var d = Detail(); var bad = fault switch
        {
            "company" => d with { CompanyId = OtherCompany }, "id" => d with { ExceptionId = Guid.NewGuid() },
            "digest" => d with { SourceFactsDigest = new string('b', 64) }, "action" => d with { ApprovalSupported = false },
            _ => d with { Exception = null! }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source(new Owner(bad)).ReadDecisionAsync(Context(), d.ExceptionId, default));
    }
    [Fact]
    public async Task Valid_found_requester_from_a_polluted_owner_is_authority_denial()
    {
        var d = Detail(); var owner = new Owner(d);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Source(owner, user: Maker).ReadDecisionAsync(Context(Maker), d.ExceptionId, default));
        Assert.Equal(1, owner.Reads); Assert.Equal(0, owner.HistoryReads);
    }
    [Fact]
    public async Task Missing_optional_owner_and_foreign_scope_do_not_read_a_fallback_store()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source(null).ReadDecisionAsync(Context(), Detail().ExceptionId, default));
        var owner = new Owner(Detail());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Source(owner).ReadDecisionAsync(Context() with { CompanyId = OtherCompany }, Detail().ExceptionId, default));
        Assert.Equal(0, owner.Reads);
    }
    [Fact]
    public async Task Late_completed_owner_callback_rechecks_company_permission_and_cancellation()
    {
        var d = Detail(); var owner = new Owner(d); var granted = true; var authority = new Authority(_ => granted);
        owner.AfterRead = () => granted = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Source(owner, authority).ReadDecisionAsync(Context(), d.ExceptionId, default));
        using var cancel = new CancellationTokenSource(); owner.AfterRead = cancel.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(owner).ReadDecisionAsync(Context(), d.ExceptionId, cancel.Token));
    }
    [Theory]
    [InlineData("ref")] [InlineData("rule")] [InlineData("label")] [InlineData("version")] [InlineData("pending")]
    public void Scalar_proof_binds_the_native_formatter_and_snapshot_identity(string fault)
    {
        var d = Detail(); var p = SalesRiskExceptionDecisionBindingV1.Proof(d); var item = SalesRiskExceptionDecisionBindingV1.Item(p);
        var snapshot = new WorkApprovalDecisionSnapshotV1(Company, item, "1", true) { SalesRiskExceptionProof = p };
        Assert.True(SalesRiskExceptionDecisionBindingV1.MatchesDecisionSnapshot(snapshot, d));
        var bad = fault switch
        {
            "ref" => snapshot with { Item = item with { References = [item.References[0] with { RecordId = Guid.NewGuid() }] } },
            "rule" => snapshot with { Item = item with { Rules = [item.Rules[0] with { RuleCode = "sales.other" }] } },
            "label" => snapshot with { Item = item with { DocumentLabel = "different" } },
            "version" => snapshot with { SourceVersion = "2" }, _ => snapshot with { Pending = false }
        };
        Assert.False(SalesRiskExceptionDecisionBindingV1.MatchesDecisionSnapshot(bad, d));
    }
    private static SalesRiskExceptionDecisionDetailV1 Detail(string state = "Draft") => SalesRiskExceptionService.ProjectRiskDecision(
        new(Company, "SENTETIK SO-5", 4, state, "Approved", false,
            new(Guid.Parse("f5050000-0000-4000-8000-000000000011"), Guid.Parse("f5050000-0000-4000-8000-000000000012"), 1,
                Guid.Parse("f5050000-0000-4000-8000-000000000013"), "Requested", 1, "  SENTETIK risk  ", Maker, T0, null, null, null, null), null), new(2026, 11, 20));
    private static SalesRiskExceptionWorkInboxSourceV1 Source(Owner? owner, Authority? authority = null, string user = Approver) =>
        new(new Store(), new CompanyContext(Company), new User(user, Company), authority ?? new Authority(_ => true), owner);
    // Controlled read protocol only: never stands in for an actual native command or SQL transaction.
    private sealed class Owner(SalesRiskExceptionDecisionDetailV1 detail) : ISalesRiskExceptionDecisionReadV1
    {
        internal int Reads; internal int HistoryReads; internal Action? AfterRead;
        public async Task<SalesRiskExceptionDecisionReadResultV1> ReadRiskExceptionDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { Reads++; await fence(token); AfterRead?.Invoke(); return new(SalesRiskExceptionDecisionReadStateV1.Found, detail); }
        public Task<SalesRiskExceptionOriginalVersionReadResultV1> ReadRiskExceptionOriginalVersionAsync(Guid id, string version, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { HistoryReads++; return Task.FromResult(new SalesRiskExceptionOriginalVersionReadResultV1(SalesRiskExceptionDecisionReadStateV1.NotFound, null)); }
    }
}
