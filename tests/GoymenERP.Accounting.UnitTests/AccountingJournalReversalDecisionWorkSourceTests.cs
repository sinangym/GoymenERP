using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingJournalReversalDecisionWorkSourceTests
{
    private static readonly Guid Proposal = Guid.Parse("b7219039-05d9-4d7a-8835-13c19f481901");
    private static readonly Guid Journal = Guid.Parse("b7219039-05d9-4d7a-8835-13c19f481902");
    private static readonly Guid Period = Guid.Parse("b7219039-05d9-4d7a-8835-13c19f481903");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Pending_and_terminal_reads_reuse_native_caption_rules_refs_and_keep_primary_native_link(bool approved)
    {
        await using var kit = new Kit(); var authority = new Kit.FixedAuthority(true, true); var fence = kit.Fence(authority: authority);
        var owner = new Owner { Detail = Detail(approved) };
        var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, owner);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(Kit.Context(), Proposal, default));
        Assert.Equal(!approved, snapshot.Pending); Assert.Equal(approved ? "2" : "1", snapshot.SourceVersion);
        Assert.Equal(approved ? 2 : 1, snapshot.JournalReversalProof!.Version);
        Assert.Equal(AccountingWorkInboxFence.Link(AccountingJournalReversalWorkInboxSourceV1.ScreenPath, Proposal), snapshot.Item.Link);
        Assert.Equal(new[] { "journal", "period" }, snapshot.Item.References.Select(r => r.Role));
        Assert.Equal(Journal, snapshot.Item.References[0].RecordId); Assert.Equal(Period, snapshot.Item.References[1].RecordId);
        Assert.Equal(AccountingJournalReversalWorkInboxSourceV1.ApprovalRequiredRule, Assert.Single(snapshot.Item.Rules).RuleCode);
        Assert.Equal(Kit.Maker.ToString("D"), snapshot.Item.PreparedBy); Assert.Null(snapshot.Item.Scope);
        Assert.True(AccountingJournalReversalDecisionBindingV1.MatchesDecisionItem(snapshot.Item, owner.Detail));
        Assert.True(AccountingJournalReversalDecisionBindingV1.MatchesDecisionProof(snapshot.Item, snapshot.JournalReversalProof));
        Assert.DoesNotContain("journalReversalProof", JsonSerializer.Serialize(snapshot, Json), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(owner.Detail.Proposal.Reason, JsonSerializer.Serialize(snapshot, Json), StringComparison.Ordinal);
        Assert.All(authority.Asked, p => Assert.Equal(AccountingJournalReversalPermissionNamesV1.Approve, p.Permission));
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task No_cancellation_actor_pending_projection_is_exactly_the_existing_native_list_item()
    {
        await using var kit = new Kit(); var detail = Detail(false); var v = detail.Proposal;
        await kit.SeedAsync(Kit.Company, db => db.JournalReversalProposals.Add(new AccountingJournalReversalProposalRow
        { Id = v.ProposalId, CompanyId = v.CompanyId, SourceJournalId = v.SourceJournalId, PeriodId = v.PeriodId,
            TransactionDate = v.TransactionDate, AccountingDate = v.AccountingDate, ValueDate = v.ValueDate, Reason = v.Reason,
            ProposedByUserId = v.ProposedBy, ProposedAtUtc = v.ProposedAtUtc, Status = 1, Version = 1 }));
        var fence = kit.Fence(); var owner = new Owner { Detail = detail }; var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, owner);
        var native = Assert.Single((await source.ListPendingAsync(Kit.Context(), 10, default)).Items!);
        var snapshot = (await source.ReadDecisionAsync(Kit.Context(), Proposal, default))!;
        Assert.Equal(JsonSerializer.Serialize(native, Json), JsonSerializer.Serialize(snapshot.Item, Json));
    }

    [Theory] [InlineData("maker")] [InlineData("cancellation-maker")]
    public async Task Purpose_preserves_actual_maker_and_KLN_D17_cancellation_actor_exclusions(string own)
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var detail = Detail(false);
        detail = own == "maker" ? Rehash(detail with { Proposal = detail.Proposal with { ProposedBy = Kit.Caller } })
            : detail with { CancellationSourceActorId = Kit.Caller };
        var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = detail });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), Proposal, default));
    }

    [Theory] [InlineData("company")] [InlineData("id")] [InlineData("digest")] [InlineData("preview")]
    [InlineData("status")] [InlineData("missing-exclusion-proof")] [InlineData("empty-excluded")]
    public async Task Malformed_owner_facts_never_fall_back_to_local_rows(string fault)
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var detail = Detail(false);
        detail = fault switch
        {
            "company" => Rehash(detail with { Proposal = detail.Proposal with { CompanyId = Kit.OtherCompany } }),
            "id" => Rehash(detail with { Proposal = detail.Proposal with { ProposalId = Guid.NewGuid() } }),
            "digest" => detail with { SourceFactsDigest = new string('B', 64) },
            "preview" => detail with { PreviewDigest = new string('B', 64) },
            "status" => Rehash(detail with { Proposal = detail.Proposal with { Status = "Rejected" } }),
            "missing-exclusion-proof" => detail with { CancellationSourceActorProofEstablished = false },
            _ => detail with { CancellationSourceActorId = Guid.Empty },
        };
        var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = detail });
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), Proposal, default));
        Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData(AccountingApiStatus.Denied)] [InlineData(AccountingApiStatus.NotFound)] [InlineData(AccountingApiStatus.Unavailable)]
    public async Task Native_owner_refusals_keep_denied_notfound_unavailable_distinct(AccountingApiStatus status)
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, new Owner { Status = status });
        if (status == AccountingApiStatus.Denied) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), Proposal, default));
        else if (status == AccountingApiStatus.NotFound) Assert.Null(await source.ReadDecisionAsync(Kit.Context(), Proposal, default));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), Proposal, default));
        Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task After_owner_await_fresh_source_fence_prevents_late_release(string fault)
    {
        await using var kit = new Kit(); kit.Fence(); var company = new MutableCompany(); var user = new MutableUser(); var authority = new MutableAuthority();
        var fence = new AccountingWorkInboxFence(company, user, authority); using var cancellation = new CancellationTokenSource();
        var owner = new Owner { Detail = Detail(false), AfterRead = () =>
        {
            if (fault == "permission") authority.Allowed = false;
            if (fault == "company") company.Set(Kit.OtherCompany);
            if (fault == "actor") user.Actor = Guid.NewGuid();
            if (fault == "member") user.Member = false;
            if (fault == "cancel") cancellation.Cancel();
        } };
        var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, owner);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadDecisionAsync(Kit.Context(), Proposal, cancellation.Token));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), Proposal, default));
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Scoped_or_missing_owner_cannot_open_companywide_purpose()
    {
        await using var kit = new Kit(); var fence = kit.Fence();
        var missing = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => missing.ReadDecisionAsync(Kit.Context(), Proposal, default));
        var owner = new Owner { Detail = Detail(false) }; var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, owner);
        var scoped = Kit.Context() with { GrantScope = new(new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>()) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(scoped, Proposal, default)); Assert.Equal(0, owner.Reads);
    }

    [Theory] [InlineData("caption")] [InlineData("maker")] [InlineData("link")] [InlineData("actual")] [InlineData("threshold")]
    [InlineData("deviation")] [InlineData("unit")] [InlineData("ref-id")] [InlineData("ref-label")] [InlineData("ref-role")]
    [InlineData("ref-link")] [InlineData("excluded")] [InlineData("scope")]
    public async Task GET_and_precommand_helpers_bind_every_actual_formatter_field(string fault)
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var detail = Detail(false) with { CancellationSourceActorId = Guid.NewGuid() };
        var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = detail });
        var snapshot = (await source.ReadDecisionAsync(Kit.Context(), Proposal, default))!; var item = Change(snapshot.Item, fault);
        Assert.False(AccountingJournalReversalDecisionBindingV1.MatchesDecisionItem(item, detail));
        Assert.False(AccountingJournalReversalDecisionBindingV1.MatchesDecisionProof(item, snapshot.JournalReversalProof));
    }

    [Theory] [InlineData("valid")] [InlineData("unestablished")] [InlineData("wrong-actor")] [InlineData("wrong-source")]
    [InlineData("source-digest")] [InlineData("result-digest")] [InlineData("wrong-exclusion")]
    public async Task POST_formatter_uses_genuine_owner_receipt_including_actual_cancellation_actor(string fault)
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var detail = Detail(true) with { CancellationSourceActorId = Guid.NewGuid() };
        var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = detail });
        var item = (await source.ReadDecisionAsync(Kit.Context(), Proposal, default))!.Item;
        var view = detail.Proposal; var answer = new AccountingApiResult<AccountingJournalReversalProposalViewV1>(AccountingApiStatus.Ok, view, false, null);
        var command = new AccountingCommandRecord(AccountingJournalReversalCommandNamesV1.Approve,
            AccountingJournalReversalServiceV1.JournalReversalApprovalKeyHash("key"), Kit.CallerText,
            AccountingJournalReversalServiceV1.JournalReversalApprovalPayloadHash(Proposal, 1), JsonSerializer.Serialize(answer, Json), "original");
        var record = new AccountingJournalReversalProposalV1(view.ProposalId, view.CompanyId, view.SourceJournalId, view.ReversalJournalId,
            view.PeriodId, view.TransactionDate, view.AccountingDate, view.ValueDate, view.Reason, view.ProposedBy, view.ProposedAtUtc,
            AccountingJournalReversalProposalStatusV1.Approved, view.DecidedBy, view.DecidedAtUtc, view.DecisionReason, view.Version);
        var receipt = AccountingJournalReversalServiceV1.BindJournalReversalApprovalReceipt(command,
            new(record, true, true, "original", detail.CancellationSourceActorId), answer, 1, "key", Kit.Caller)!;
        Assert.NotNull(receipt);
        receipt = fault switch
        {
            "unestablished" => receipt with { CancellationSourceActorProofEstablished = false },
            "wrong-actor" => receipt with { ActorId = Kit.Maker }, "wrong-source" => receipt with { SourceJournalId = Guid.NewGuid() },
            "source-digest" => receipt with { SourceFactsDigest = new string('B', 64) }, "result-digest" => receipt with { OriginalResultDigest = new string('B', 64) },
            "wrong-exclusion" => receipt with { CancellationSourceActorId = Guid.NewGuid() }, _ => receipt,
        };
        Assert.Equal(fault == "valid", AccountingJournalReversalDecisionBindingV1.MatchesDecisionItem(item, view, receipt));
    }

    [Fact]
    public async Task Scalar_proof_has_no_reason_or_dates_and_does_not_pretend_to_certify_their_full_immutable_hash()
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = Detail(false) });
        var snapshot = (await source.ReadDecisionAsync(Kit.Context(), Proposal, default))!;
        Assert.True(AccountingJournalReversalDecisionBindingV1.MatchesDecisionProof(snapshot.Item,
            snapshot.JournalReversalProof! with { SourceFactsDigest = new string('B', 64) }));
        // The precommand helper certifies formatter fields only. Genuine native receipt binds all immutable facts after execution.
        Assert.DoesNotContain("reason", JsonSerializer.Serialize(snapshot, Json), StringComparison.OrdinalIgnoreCase);
    }

    private static WorkInboxItemV1 Change(WorkInboxItemV1 item, string fault) => fault switch
    {
        "caption" => item with { DocumentLabel = "wrong" }, "maker" => item with { PreparedBy = Kit.CallerText }, "link" => item with { Link = "/wrong" },
        "actual" => item with { Rules = [item.Rules[0] with { Actual = "1" }] }, "threshold" => item with { Rules = [item.Rules[0] with { Threshold = "1" }] },
        "deviation" => item with { Rules = [item.Rules[0] with { Deviation = "1" }] }, "unit" => item with { Rules = [item.Rules[0] with { Unit = "kg" }] },
        "excluded" => item with { ExcludedDeciders = [Kit.CallerText] }, "scope" => item with { Scope = new(Guid.NewGuid(), null, null) },
        _ => item with { References = item.References.Select((r, i) => i != 0 ? r : fault switch
            { "ref-id" => r with { RecordId = Guid.NewGuid() }, "ref-label" => r with { Label = "wrong" }, "ref-role" => r with { Role = "other" }, _ => r with { Link = "/wrong" } }).ToArray() },
    };
    private static AccountingJournalReversalDecisionDetailV1 Detail(bool approved)
    {
        var view = new AccountingJournalReversalProposalViewV1(Proposal, Kit.Company, Journal, approved ? Guid.NewGuid() : null, Period,
            new(2026, 11, 20), new(2026, 11, 20), null, "SENTETIK immutable reversal reason", Kit.Maker, Kit.Start.ToUniversalTime(),
            approved ? "Approved" : "Pending", approved ? Kit.Caller : null, approved ? Kit.Start.AddHours(1).ToUniversalTime() : null, null, approved ? 2 : 1);
        return Rehash(new(view, "", "") { CancellationSourceActorProofEstablished = true });
    }
    private static AccountingJournalReversalDecisionDetailV1 Rehash(AccountingJournalReversalDecisionDetailV1 detail) => detail with
    { SourceFactsDigest = AccountingJournalReversalServiceV1.JournalReversalSourceFactsDigest(detail.Proposal),
        PreviewDigest = AccountingJournalReversalServiceV1.JournalReversalPreviewDigest(detail.Proposal) };
    private sealed class Owner : IAccountingJournalReversalDecisionReadV1
    {
        public AccountingJournalReversalDecisionDetailV1 Detail = null!; public AccountingApiStatus Status = AccountingApiStatus.Ok; public int Reads; public Action? AfterRead;
        public async Task<AccountingApiResult<AccountingJournalReversalDecisionDetailV1>> ReadDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { Reads++; await fence(token); AfterRead?.Invoke(); return new(Status, Status == AccountingApiStatus.Ok ? Detail : null, false, null); }
    }
    private sealed class MutableCompany : ICompanyContext
    { public bool IsResolved => true; public Guid CompanyId { get; private set; } = Kit.Company; public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    { public Guid Actor = Kit.Caller; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Kit.Company }; public bool CanAccessCompany(Guid id) => Member && id == Kit.Company; }
    private sealed class MutableAuthority : IErpAuthorityRevalidationService
    { public bool Allowed = true; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        => Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
}
