using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

public sealed class NormalJournalDraftPublicApiV1Tests
{
    private static readonly Guid Company = Guid.Parse("f1000000-0000-4000-8000-000000000001");
    private static readonly Guid Actor = Guid.Parse("f1000000-0000-4000-8000-000000000002");
    private static readonly Guid Draft = Guid.Parse("f1000000-0000-4000-8000-000000000003");
    private static readonly Guid Source = Guid.Parse("f1000000-0000-4000-8000-000000000004");
    private static readonly Guid Debit = Guid.Parse("f1000000-0000-4000-8000-000000000005");
    private static readonly Guid Credit = Guid.Parse("f1000000-0000-4000-8000-000000000006");
    private static readonly Guid Currency = Guid.Parse("f1000000-0000-4000-8000-000000000007");

    [Fact]
    public async Task Missing_real_server_source_fails_closed_without_calling_the_lifecycle()
    {
        var lifecycle = new CapturingLifecycle();
        var service = Service(lifecycle, new UnavailableNormalJournalDraftServerSourceProviderV1());

        var result = await service.CreateAsync(Request(ServerSource()), "source-missing");

        Assert.Equal(AccountingApiStatus.Unavailable, result.Status);
        Assert.Null(lifecycle.Created);
    }

    [Fact]
    public async Task Public_fields_are_bound_to_the_exact_server_rule_without_accepting_mapping_metadata()
    {
        var lifecycle = new CapturingLifecycle();
        var source = ServerSource();
        var service = Service(lifecycle, new FixedSource(source));

        var result = await service.CreateAsync(Request(source), "create-1");

        Assert.Equal(AccountingApiStatus.Created, result.Status);
        var created = Assert.IsType<NormalJournalDraftV1>(lifecycle.Created);
        Assert.Equal(Company, created.CompanyId);
        Assert.Equal(Actor, created.PreparedByUserId);
        Assert.Equal(1, created.Version);
        Assert.Same(source.Rule, created.Rule);
        Assert.Equal(Currency, created.Request.CurrencyId);
        Assert.Equal("approved-purpose", created.Request.Source.PostingPurpose);
        Assert.Equal("approved-rule-v1", created.Request.PostingRuleVersion);
        Assert.Collection(created.Request.Lines,
            line => Assert.Equal(("approved-debit", PostingSide.Debit, 10.25m),
                (line.MappingKey, line.Side, line.Amount)),
            line => Assert.Equal(("approved-credit", PostingSide.Credit, 10.25m),
                (line.MappingKey, line.Side, line.Amount)));
    }

    [Theory]
    [InlineData("1e2")]
    [InlineData("+1")]
    [InlineData("1,00")]
    [InlineData("0")]
    public async Task Non_lossless_or_non_positive_amount_text_is_refused_before_source_resolution(string amount)
    {
        var provider = new CountingSource(ServerSource());
        var service = Service(new CapturingLifecycle(), provider);
        var request = Request(ServerSource()) with
        {
            Lines =
            [
                new CreateNormalJournalDraftLineRequestV1(Debit, "Debit", amount),
                new CreateNormalJournalDraftLineRequestV1(Credit, "Credit", "10.25"),
            ],
        };

        var result = await service.CreateAsync(request, "invalid-amount");

        Assert.Equal(AccountingApiStatus.Invalid, result.Status);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Stale_preparation_is_refused_before_lifecycle_write()
    {
        var lifecycle = new CapturingLifecycle();
        var source = ServerSource();
        var service = Service(lifecycle, new FixedSource(source));
        var request = Request(source) with { ExpectedPreparationVersion = new string('b', 64) };

        var result = await service.CreateAsync(request, "stale-preparation");

        Assert.Equal(AccountingApiStatus.Conflict, result.Status);
        Assert.Equal(NormalJournalDraftPreparationCodesV1.VersionConflict, result.Code);
        Assert.Null(lifecycle.Created);
    }

    [Fact]
    public async Task Stored_v2_replay_returns_before_current_source_resolution()
    {
        var source = ServerSource();
        var lifecycle = new CapturingLifecycle
        {
            Replay = new AccountingApiResult<NormalJournalDraftViewV1>(
                AccountingApiStatus.Created, null, true, null),
        };
        var provider = new CountingSource(source);
        var service = Service(lifecycle, provider);

        var result = await service.CreateAsync(Request(source), "stored-replay");

        Assert.Equal(AccountingApiStatus.Created, result.Status);
        Assert.True(result.IsReplay);
        Assert.Equal(0, provider.Calls);
    }

    private static NormalJournalDraftPublicServiceV1 Service(
        INormalJournalDraftLifecycleV1 lifecycle,
        INormalJournalDraftServerSourceProviderV1 provider)
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new NormalJournalDraftPublicServiceV1(
            lifecycle,
            provider,
            new NormalJournalPermissionBindingV1(),
            new GrantedAuthority(),
            company,
            new FixedUser(),
            new FixedTimeProvider());
    }

    private static CreateNormalJournalDraftRequestV1 Request(NormalJournalDraftServerSourceV1 source) => new(
        Draft,
        Source,
        new DateOnly(2026, 9, 24),
        new DateOnly(2026, 9, 24),
        null,
        NormalJournalDraftSourcePreparationV1.TryCreate(Company, Source, source)!.PreparationVersion,
        [
            new CreateNormalJournalDraftLineRequestV1(Debit, "Debit", "10.25"),
            new CreateNormalJournalDraftLineRequestV1(Credit, "Credit", "10.25"),
        ]);

    private static NormalJournalDraftServerSourceV1 ServerSource()
    {
        var at = DateTimeOffset.Parse("2026-09-24T12:00:00Z");
        var debit = new AccountRef(Company, Debit);
        var credit = new AccountRef(Company, Credit);
        var mirror = new AccountPlanMirrorV1(Company, "mikro-approved-v1", new string('a', 64), Actor, at,
        [
            new AccountPlanMirrorRowV1(debit, "SYNTHETIC-001", "Synthetic debit"),
            new AccountPlanMirrorRowV1(credit, "SYNTHETIC-002", "Synthetic credit"),
        ]);
        var rule = new NormalJournalPostingRuleV1(mirror, "approved-purpose", "approved-rule-v1", Actor, at,
        [
            new NormalJournalMappingV1("approved-debit", PostingSide.Debit, debit),
            new NormalJournalMappingV1("approved-credit", PostingSide.Credit, credit),
        ]);
        return new NormalJournalDraftServerSourceV1(rule, Currency, "2", "finance-profile", "7");
    }

    private sealed class FixedSource(NormalJournalDraftServerSourceV1 source)
        : INormalJournalDraftServerSourceProviderV1
    {
        public Task<NormalJournalDraftServerSourceV1?> ResolveAsync(
            Guid companyId, Guid sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<NormalJournalDraftServerSourceV1?>(source);
    }

    private sealed class CountingSource(NormalJournalDraftServerSourceV1 source)
        : INormalJournalDraftServerSourceProviderV1
    {
        public int Calls { get; private set; }
        public Task<NormalJournalDraftServerSourceV1?> ResolveAsync(
            Guid companyId, Guid sourceId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<NormalJournalDraftServerSourceV1?>(source);
        }
    }

    private sealed class CapturingLifecycle : INormalJournalDraftLifecycleV1
    {
        public NormalJournalDraftV1? Created { get; private set; }
        public AccountingApiResult<NormalJournalDraftViewV1>? Replay { get; init; }

        public Task<AccountingApiResult<NormalJournalDraftViewV1>> CreateAsync(
            NormalJournalDraftV1? draft, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            Created = draft;
            return Task.FromResult(new AccountingApiResult<NormalJournalDraftViewV1>(
                AccountingApiStatus.Created, null, false, null));
        }

        public Task<AccountingApiResult<NormalJournalDraftViewV1>?> TryReplayPreparedCreateAsync(
            NormalJournalPreparedCreateCommandV2 command,
            string idempotencyKey,
            CancellationToken cancellationToken = default) => Task.FromResult(Replay);

        public Task<AccountingApiResult<NormalJournalDraftViewV1>> CreatePreparedAsync(
            NormalJournalDraftV1? draft,
            NormalJournalPreparedCreateCommandV2 command,
            string idempotencyKey,
            CancellationToken cancellationToken = default) => CreateAsync(draft, idempotencyKey, cancellationToken);

        public Task<AccountingApiResult<NormalJournalDraftViewV1>> ReadAsync(
            Guid draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AccountingApiResult<NormalJournalDraftViewV1>> ApproveAsync(
            Guid draftId, long expectedVersion, Guid periodId, string idempotencyKey,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class GrantedAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string>(["ACC-V1"]),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-09-24T13:00:00Z");
    }
}
