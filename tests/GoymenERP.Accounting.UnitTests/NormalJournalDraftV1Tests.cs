using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

public sealed class NormalJournalDraftV1Tests
{
    // All ids, codes, permissions and mappings in this file are synthetic unit-test inputs only.
    private static readonly Guid Company = Guid.Parse("a0000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Preparer = Guid.Parse("a0000000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherActor = Guid.Parse("a0000000-0000-4000-8000-0000000000b2");
    private static readonly Guid Currency = Guid.Parse("a0000000-0000-4000-8000-0000000000c1");
    private static readonly DateTimeOffset At = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
    private const string Create = "SENTETIK.normal-draft.create";
    private const string Approve = "SENTETIK.normal-draft.approve";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actual_narrow_approve_authority_allows_own_or_another_normal_draft(bool own)
    {
        var f = new Fixture();
        f.User.UserId = (own ? Preparer : OtherActor).ToString("D");
        var draft = f.Prepare().Draft!;
        var result = await f.Policy.CheckAsync(draft, 7);
        Assert.Equal(AccountingApiStatus.Ok, result.Status);
        Assert.Same(draft, result.Resource);
        Assert.False(result.IsReplay); // A policy check is not a stored command/approval.
        Assert.Equal(new[] { Approve, Approve }, f.Authority.Requests);
        Assert.Equal(7, draft.Version);
        Assert.Equal(JournalKind.Original, draft.Kind);
    }

    [Fact]
    public async Task Create_permission_alone_never_implies_approval()
    {
        var f = new Fixture();
        f.Authority.Grant = permission => permission == Create;
        var result = await f.Policy.CheckAsync(f.Prepare().Draft!, 7);
        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(new[] { Approve }, f.Authority.Requests);
    }

    [Fact]
    public async Task Version_conflict_reports_exact_versions_only_after_authorization()
    {
        var f = new Fixture();
        var result = await f.Policy.CheckAsync(f.Prepare().Draft!, 6);
        Assert.Equal(AccountingApiStatus.Conflict, result.Status);
        Assert.Equal(AccountingApiCodes.VersionConflict, result.Code);
        Assert.Equal(6L, result.ExpectedVersion);
        Assert.Equal(7L, result.CurrentVersion);
        Assert.Null(result.Resource);
        f.Authority.Grant = _ => false;
        var denied = await f.Policy.CheckAsync(f.Prepare().Draft!, 6);
        Assert.Equal(AccountingApiStatus.Denied, denied.Status);
        Assert.Null(denied.ExpectedVersion);
        Assert.Null(denied.CurrentVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Rejects_invalid_expected_version(long version)
    {
        var f = new Fixture();
        var result = await f.Policy.CheckAsync(f.Prepare().Draft!, version);
        Assert.Equal(AccountingApiStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Unknown_and_foreign_drafts_are_the_same_codeless_not_found()
    {
        var f = new Fixture();
        var other = new Fixture(Guid.NewGuid());
        var absent = await f.Policy.CheckAsync(null, 7);
        var foreign = await f.Policy.CheckAsync(other.Prepare().Draft!, 7);
        Assert.Equal(AccountingApiStatus.NotFound, absent.Status);
        Assert.Equal(absent, foreign);
        Assert.Null(foreign.Code);
        Assert.Null(foreign.CurrentVersion);
    }

    [Theory]
    [InlineData("unresolved")]
    [InlineData("anonymous")]
    [InlineData("bad-actor")]
    [InlineData("empty-actor")]
    [InlineData("company-access")]
    public async Task Missing_context_or_actor_is_denied_before_authority(string change)
    {
        var f = new Fixture();
        if (change == "unresolved") f.Context.IsResolved = false;
        if (change == "anonymous") f.User.IsAuthenticated = false;
        if (change == "bad-actor") f.User.UserId = "not-a-guid";
        if (change == "empty-actor") f.User.UserId = Guid.Empty.ToString("D");
        if (change == "company-access") f.User.Allowed.Clear();
        var result = await f.Policy.CheckAsync(f.Prepare().Draft!, 7);
        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Empty(f.Authority.Requests);
    }

    [Theory]
    [InlineData(1, "company")]
    [InlineData(2, "company")]
    [InlineData(1, "actor")]
    [InlineData(2, "actor")]
    [InlineData(2, "grant")]
    [InlineData(2, "scope")]
    [InlineData(2, "access")]
    public async Task Revalidation_fences_changes_during_each_await(int atCall, string change)
    {
        var f = new Fixture();
        f.Authority.AfterRead = count =>
        {
            if (count != atCall) return;
            if (change == "company") f.Context.Set(Guid.NewGuid());
            if (change == "actor") f.User.UserId = OtherActor.ToString("D");
            if (change == "grant") f.Authority.Grant = _ => false;
            if (change == "scope") f.Authority.CompanyWide = false;
            if (change == "access") f.User.Allowed.Clear();
        };
        var result = await f.Policy.CheckAsync(f.Prepare().Draft!, 7);
        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Site_only_authority_is_not_a_company_wide_approval()
    {
        var f = new Fixture();
        f.Authority.CompanyWide = false;
        Assert.Equal(AccountingApiStatus.Denied, (await f.Policy.CheckAsync(f.Prepare().Draft!, 7)).Status);
    }

    [Fact]
    public async Task Cancellation_and_authority_failure_never_return_eligibility()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.Authority.AfterRead = _ => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Policy.CheckAsync(f.Prepare().Draft!, 7, cancellation.Token));
        f.Authority.AfterRead = _ => throw new InvalidOperationException("Synthetic authority unavailable");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Policy.CheckAsync(f.Prepare().Draft!, 7));
    }

    [Fact]
    public void Existing_period_or_read_authority_cannot_be_repurposed_for_normal_approval()
    {
        foreach (var permission in AccountingPermissionManifestV1.All)
        {
            Assert.Throws<ArgumentException>(() => new NormalJournalPermissionBindingV1(Create, permission));
            Assert.Throws<ArgumentException>(() => new NormalJournalPermissionBindingV1(permission, Approve));
        }
        Assert.Throws<ArgumentException>(() => new NormalJournalPermissionBindingV1(Create, Create));
    }

    [Theory]
    [InlineData(JournalKind.Reversal)]
    [InlineData(JournalKind.Correction)]
    [InlineData((JournalKind)0)]
    public void Normal_draft_factory_refuses_other_journal_kinds(JournalKind kind)
    {
        var f = new Fixture();
        Assert.Equal(AccountingErrorCodes.InvalidReference,
            Assert.Throws<AccountingDomainException>(() => f.Prepare(kind: kind)).Code);
    }

    [Fact]
    public void Missing_approved_rule_or_mapping_remains_an_explicit_exception_not_a_fallback()
    {
        var f = new Fixture();
        var noRule = NormalJournalDraftV1.Prepare(JournalKind.Original, f.Request(), 7, Preparer, f.Mirror, null);
        Assert.Null(noRule.Draft);
        Assert.Null(noRule.Pending);
        Assert.Equal(new[] { "SENTETIK-bank", "SENTETIK-party" }, noRule.Exception!.MissingMappingKeys);
        Assert.Equal(f.Request().Source, noRule.Exception.Source);
        Assert.Equal("SENTETIK-message", noRule.Exception.FirstMessageId);
        var incomplete = f.Rule([f.Entries()[0]]);
        var missing = f.Prepare(rule: incomplete);
        Assert.Null(missing.Draft);
        Assert.Equal(new[] { "SENTETIK-party" }, missing.Exception!.MissingMappingKeys);
        var nullAccount = f.Request() with { Lines = [f.Lines()[0] with { Account = null }, f.Lines()[1]] };
        Assert.NotNull(f.Prepare(request: nullAccount).Exception);
    }

    [Fact]
    public void An_unknown_amount_is_pending_valuation_not_zero()
    {
        var f = new Fixture();
        var request = f.Request() with { Lines = [f.Lines()[0] with { Amount = null }, f.Lines()[1]] };
        var result = f.Prepare(request: request);
        Assert.Null(result.Draft);
        Assert.Null(result.Exception);
        Assert.Equal(1, result.Pending!.LinesAwaitingAmount);
    }

    [Fact]
    public void Mapping_and_draft_snapshots_do_not_retain_mutable_caller_arrays()
    {
        var f = new Fixture();
        var entries = f.Entries();
        var rule = f.Rule(entries);
        var original = entries[0];
        entries[0] = original with { Account = entries[1].Account };
        Assert.Equal(original, rule.Mappings[0]);
        var lines = f.Lines();
        var draft = f.Prepare(request: f.Request() with { Lines = lines }, rule: rule).Draft!;
        lines[0] = lines[0] with { Amount = 1m };
        Assert.Equal(12.3456m, draft.TotalDebit);
        Assert.Equal(draft.TotalDebit, draft.TotalCredit);
        Assert.Equal(12.3456m, draft.Request.Lines[0].Amount);
        Assert.Throws<NotSupportedException>(() => ((IList<JournalLine>)draft.Lines)[0] =
            new JournalLine(f.Mirror.Rows[0].Account, 1m, 0m));
    }

    [Fact]
    public void Explicit_account_mismatch_rule_version_purpose_or_mirror_change_is_refused()
    {
        var f = new Fixture();
        var request = f.Request();
        var wrongAccount = request with { Lines = [f.Lines()[0] with { Account = f.Mirror.Rows[1].Account }, f.Lines()[1]] };
        Assert.Throws<AccountingDomainException>(() => f.Prepare(request: wrongAccount));
        Assert.Throws<AccountingDomainException>(() => f.Prepare(request: request with { PostingRuleVersion = "other" }));
        Assert.Throws<AccountingDomainException>(() => f.Prepare(request: request with
        { Source = new SourceReference(f.Mirror.CompanyId, request.Source.SourceId, "other") }));
        var changed = new AccountPlanMirrorV1(f.Mirror.CompanyId, "changed", new string('b', 64), Preparer, At, f.Mirror.Rows);
        Assert.Throws<AccountingDomainException>(() => NormalJournalDraftV1.Prepare(JournalKind.Original,
            request, 7, Preparer, changed, f.Rule(f.Entries())));
    }

    [Fact]
    public void Separately_rehydrated_identical_mirror_is_accepted_but_rebound_codes_are_not()
    {
        var f = new Fixture();
        var mirror = new AccountPlanMirrorV1(f.Mirror.CompanyId, f.Mirror.SourceVersion, f.Mirror.SourceSha256,
            f.Mirror.ApprovedByUserId, f.Mirror.ApprovedAtUtc,
            f.Mirror.Rows.Select(row => new AccountPlanMirrorRowV1(row.Account, row.Code, row.Name)));
        var rule = f.Rule(f.Entries());
        Assert.NotNull(NormalJournalDraftV1.Prepare(JournalKind.Original, f.Request(), 7, Preparer, mirror, rule).Draft);
        var rebound = new AccountPlanMirrorV1(f.Mirror.CompanyId, f.Mirror.SourceVersion, f.Mirror.SourceSha256,
            f.Mirror.ApprovedByUserId, f.Mirror.ApprovedAtUtc,
            f.Mirror.Rows.Select(row => new AccountPlanMirrorRowV1(row.Account, row.Code + "-changed", row.Name)));
        Assert.Throws<AccountingDomainException>(() =>
            NormalJournalDraftV1.Prepare(JournalKind.Original, f.Request(), 7, Preparer, rebound, rule));
    }

    [Fact]
    public void Rule_requires_known_mirror_accounts_and_unambiguous_user_approved_mapping()
    {
        var f = new Fixture();
        Assert.Throws<AccountingDomainException>(() => f.Rule([f.Entries()[0], f.Entries()[0]]));
        Assert.Throws<AccountingDomainException>(() => f.Rule([f.Entries()[0] with { Account = new AccountRef(Company, Guid.NewGuid()) }]));
        Assert.Throws<AccountingDomainException>(() => new NormalJournalPostingRuleV1(f.Mirror, "SENTETIK-purpose", "rule-v1",
            Guid.Empty, At, f.Entries()));
        Assert.Throws<AccountingDomainException>(() => new NormalJournalPostingRuleV1(f.Mirror, "SENTETIK-purpose", "rule-v1",
            Preparer, default, f.Entries()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_known_amount_is_not_a_valid_draft(int amount)
    {
        var f = new Fixture();
        Assert.Throws<AccountingDomainException>(() => f.Prepare(request: f.Request() with
        { Lines = [f.Lines()[0] with { Amount = amount }, f.Lines()[1]] }));
    }

    [Fact]
    public void Unbalanced_or_foreign_source_is_refused_without_rounding_or_balancing_line()
    {
        var f = new Fixture();
        Assert.Equal(AccountingErrorCodes.JournalUnbalanced,
            Assert.Throws<AccountingDomainException>(() => f.Prepare(request: f.Request() with
            { Lines = [f.Lines()[0] with { Amount = 12.3457m }, f.Lines()[1]] })).Code);
        Assert.Equal(AccountingErrorCodes.CompanyMismatch,
            Assert.Throws<AccountingDomainException>(() => f.Prepare(request: f.Request() with
            { Source = new SourceReference(Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-purpose") })).Code);
    }

    private sealed class Fixture
    {
        public Fixture(Guid? company = null)
        {
            var id = company ?? Company;
            Mirror = new AccountPlanMirrorV1(id, "SENTETIK-source-v1", new string('a', 64), Preparer, At,
                [new AccountPlanMirrorRowV1(new AccountRef(id, Guid.Parse("b0000000-0000-4000-8000-000000000001")), "SENTETIK-001", "Synthetic bank"),
                 new AccountPlanMirrorRowV1(new AccountRef(id, Guid.Parse("b0000000-0000-4000-8000-000000000002")), "SENTETIK-002", "Synthetic party")]);
            Context.Set(id);
            User.Allowed.Add(id);
            Policy = new NormalJournalDraftApprovalPolicyV1(new NormalJournalPermissionBindingV1(Create, Approve),
                Authority, Context, User);
        }

        public AccountPlanMirrorV1 Mirror { get; }
        public MutableCompany Context { get; } = new();
        public MutableUser User { get; } = new();
        public FakeAuthority Authority { get; } = new();
        public NormalJournalDraftApprovalPolicyV1 Policy { get; }
        public NormalJournalMappingV1[] Entries() =>
            [new("SENTETIK-bank", PostingSide.Credit, Mirror.Rows[0].Account),
             new("SENTETIK-party", PostingSide.Debit, Mirror.Rows[1].Account)];
        public ProposedLine[] Lines() =>
            [new("SENTETIK-bank", Mirror.Rows[0].Account, PostingSide.Credit, 12.3456m),
             new("SENTETIK-party", Mirror.Rows[1].Account, PostingSide.Debit, 12.3456m)];
        public NormalJournalPostingRuleV1 Rule(IEnumerable<NormalJournalMappingV1> entries) =>
            new(Mirror, "SENTETIK-purpose", "rule-v1", Preparer, At, entries);
        public PostingRequest Request() => new("SENTETIK-consumer", "SENTETIK-message",
            Guid.Parse("c0000000-0000-4000-8000-000000000001"),
            new SourceReference(Mirror.CompanyId, Guid.Parse("c0000000-0000-4000-8000-000000000002"), "SENTETIK-purpose"),
            "rule-v1", Currency, new PostingDates(new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 24), null, At), Lines());
        public NormalJournalPreparationV1 Prepare(PostingRequest? request = null,
            NormalJournalPostingRuleV1? rule = null, JournalKind kind = JournalKind.Original) =>
            NormalJournalDraftV1.Prepare(kind, request ?? Request(), 7, Preparer, Mirror, rule ?? Rule(Entries()));
    }

    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved { get; set; } = true;
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class MutableUser : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = Preparer.ToString("D");
        public HashSet<Guid> Allowed { get; } = [];
        public IReadOnlySet<Guid> AllowedCompanyIds => Allowed;
        public bool CanAccessCompany(Guid companyId) => Allowed.Contains(companyId);
    }

    private sealed class FakeAuthority : IErpAuthorityRevalidationService
    {
        public List<string> Requests { get; } = [];
        public Func<string, bool> Grant { get; set; } = permission => permission == Approve;
        public bool CompanyWide { get; set; } = true;
        public Action<int>? AfterRead { get; set; }
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(permission);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            AfterRead?.Invoke(Requests.Count);
            return new PermissionScope(Grant(permission), CompanyWide,
                new HashSet<string>(["SENTETIK-ROLE"], StringComparer.Ordinal), new HashSet<Guid>(),
                new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
}
