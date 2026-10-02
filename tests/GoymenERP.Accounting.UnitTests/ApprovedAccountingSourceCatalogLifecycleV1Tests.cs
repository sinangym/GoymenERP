using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure;

namespace GoymenERP.Accounting.UnitTests;

public sealed class ApprovedAccountingSourceCatalogLifecycleV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("10000000-0000-4000-8000-000000000099");
    private static readonly Guid Actor = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid Package = Guid.Parse("10000000-0000-4000-8000-000000000003");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T09:00:00Z");

    [Fact]
    public async Task The_preparer_may_activate_the_same_package_with_the_separate_catalog_authority()
    {
        var store = new MemoryStore();
        var service = Service(store, new FixedAuthority(Granted()));

        var prepared = await service.PrepareAsync(Request(), "prepare-key");
        var activated = await service.ActivateAsync(Package,
            new ActivateApprovedAccountingSourceCatalogRequestV1(1, 0), "activate-key");

        Assert.Equal(AccountingApiStatus.Created, prepared.Status);
        Assert.Equal("Prepared", prepared.Resource?.Status);
        Assert.Equal(AccountingApiStatus.Ok, activated.Status);
        Assert.Equal("Active", activated.Resource?.Status);
        Assert.Equal(Actor, activated.Resource?.PreparedByUserId);
        Assert.Equal(Actor, activated.Resource?.ActivatedByUserId);
        Assert.Equal(1, activated.Resource?.CatalogVersion);
        Assert.Equal(1, store.Prepares);
        Assert.Equal(1, store.Activations);
    }

    [Fact]
    public async Task Permission_revoked_at_the_final_fence_refuses_activation_before_the_store_effect()
    {
        var store = new MemoryStore(await Preview());
        var service = Service(store, new SequencedAuthority(
            Granted(), Granted(), Granted(), Granted(), PermissionScope.Denied));

        var result = await service.ActivateAsync(Package,
            new ActivateApprovedAccountingSourceCatalogRequestV1(1, 0), "revoked-key");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(AccountingApiCodes.AuthorityDenied, result.Code);
        Assert.Equal(0, store.Activations);
        Assert.Equal(ApprovedAccountingSourceCatalogStatusV1.Prepared, store.Record?.Status);
    }

    [Fact]
    public async Task A_preview_digest_mismatch_is_refused_without_preparing_a_catalog_version()
    {
        var store = new MemoryStore();
        var service = Service(store, new FixedAuthority(Granted()));
        var request = Request() with { ExpectedAccountsSourceSha256 = new string('A', 64) };

        var result = await service.PrepareAsync(request, "digest-mismatch-key");

        Assert.Equal(AccountingApiStatus.Conflict, result.Status);
        Assert.Equal(ApprovedAccountingSourceCatalogCodesV1.DigestMismatch, result.Code);
        Assert.Equal(0, store.Prepares);
        Assert.Null(store.Record);
    }

    [Fact]
    public async Task A_128_character_source_version_is_accepted_and_129_is_field_refused_before_write()
    {
        var acceptedStore = new MemoryStore();
        var accepted = await Service(acceptedStore, new FixedAuthority(Granted()))
            .PrepareAsync(Request(new string('A', 128)), "source-version-128");
        var refusedStore = new MemoryStore();
        var refused = await Service(refusedStore, new FixedAuthority(Granted()))
            .PrepareAsync(Request(new string('A', 129)), "source-version-129");

        Assert.Equal(AccountingApiStatus.Created, accepted.Status);
        Assert.Equal(1, acceptedStore.Prepares);
        Assert.Equal(AccountingApiStatus.Invalid, refused.Status);
        Assert.Equal("accountPlanSourceVersion", refused.Field);
        Assert.Equal(0, refusedStore.Prepares);
    }

    [Fact]
    public async Task A_company_switch_during_command_log_read_refuses_without_writing_or_disclosing_a_result()
    {
        var context = new MutableCompanyContext(Company);
        var store = new MemoryStore { AfterFind = () => context.Set(OtherCompany) };
        var result = await Service(store, new FixedAuthority(Granted()), context, new MultiCompanyUser())
            .PrepareAsync(Request(), "switched-company");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(0, store.Prepares);
    }

    [Fact]
    public async Task A_company_switch_during_catalog_read_refuses_the_reopened_resource()
    {
        var context = new MutableCompanyContext(Company);
        var store = new MemoryStore(await Preview()) { AfterRead = () => context.Set(OtherCompany) };
        var result = await Service(store, new FixedAuthority(Granted()), context, new MultiCompanyUser())
            .ReadAsync(Package);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Permission_revoked_after_replay_lookup_does_not_disclose_the_stored_result()
    {
        var store = new MemoryStore();
        var first = await Service(store, new FixedAuthority(Granted()))
            .PrepareAsync(Request(), "replay-fence");
        var replay = await Service(store,
                new SequencedAuthority(Granted(), PermissionScope.Denied))
            .PrepareAsync(Request(), "replay-fence");

        Assert.Equal(AccountingApiStatus.Created, first.Status);
        Assert.Equal(AccountingApiStatus.Denied, replay.Status);
        Assert.Null(replay.Resource);
        Assert.False(replay.IsReplay);
        Assert.Equal(1, store.Prepares);
    }

    [Fact]
    public async Task A_context_switch_after_refusal_log_write_does_not_disclose_the_refusal()
    {
        var context = new MutableCompanyContext(Company);
        var store = new MemoryStore { AfterRecord = () => context.Set(OtherCompany) };
        var request = Request() with { ExpectedAccountsSourceSha256 = new string('A', 64) };

        var result = await Service(store, new FixedAuthority(Granted()), context, new MultiCompanyUser())
            .PrepareAsync(request, "refusal-log-fence");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(0, store.Prepares);
    }

    [Fact]
    public async Task A_context_switch_during_concurrent_winner_read_does_not_disclose_the_winner()
    {
        var context = new MutableCompanyContext(Company);
        var store = new MemoryStore { ThrowConcurrentWinner = true };
        store.AfterFindCall = call =>
        {
            if (call == 2) context.Set(OtherCompany);
        };

        var result = await Service(store, new FixedAuthority(Granted()), context, new MultiCompanyUser())
            .PrepareAsync(Request(), "winner-fence");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
    }

    private static ApprovedAccountingSourceCatalogServiceV1 Service(
        MemoryStore store,
        IErpAuthorityRevalidationService authority,
        ICompanyContext? companyContext = null,
        ICurrentUser? currentUser = null) =>
        new(
            new ApprovedAccountingSourcePackageParserV1(),
            store,
            store,
            authority,
            companyContext ?? new FixedCompanyContext(),
            currentUser ?? new FixedUser(),
            new FixedCorrelation(),
            new FixedTimeProvider(Now));

    private static PrepareApprovedAccountingSourceCatalogRequestV1 Request(
        string accountPlanSourceVersion = "mikro-synthetic-v1")
    {
        var accounts = Encoding.UTF8.GetBytes(
            ApprovedAccountingSourcePackageTemplateV1.AccountsHeader + "\n"
            + "20000000-0000-4000-8000-000000000001,100.01,Synthetic debit\n"
            + "20000000-0000-4000-8000-000000000002,320.01,Synthetic credit\n");
        var mappings = Encoding.UTF8.GetBytes(
            ApprovedAccountingSourcePackageTemplateV1.MappingsHeader + "\n"
            + "30000000-0000-4000-8000-000000000001,finance.manual-receipt.recorded.v1,v1,profile,profile-v1,"
            + "40000000-0000-4000-8000-000000000001,receipt,rule-v1,debit,Debit,20000000-0000-4000-8000-000000000001\n"
            + "30000000-0000-4000-8000-000000000001,finance.manual-receipt.recorded.v1,v1,profile,profile-v1,"
            + "40000000-0000-4000-8000-000000000001,receipt,rule-v1,credit,Credit,20000000-0000-4000-8000-000000000002\n");
        return new PrepareApprovedAccountingSourceCatalogRequestV1(
            Package,
            accountPlanSourceVersion,
            accounts,
            mappings,
            Convert.ToHexString(SHA256.HashData(accounts)),
            Convert.ToHexString(SHA256.HashData(mappings)));
    }

    private static async Task<ApprovedAccountingSourcePackagePreviewV1> Preview()
    {
        await Task.Yield();
        var request = Request();
        return new ApprovedAccountingSourcePackageParserV1().Preview(
            new TrustedAccountingSourceApprovalContextV1(Company, Actor, Now),
            new ApprovedAccountingSourcePackagePreviewRequestV1(
                request.AccountPlanSourceVersion,
                request.AccountsUtf8Csv,
                request.PostingMappingsUtf8Csv));
    }

    private static PermissionScope Granted() => new(
        true,
        true,
        new HashSet<string>(["SYNTHETIC-CATALOG-APPROVER"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    private sealed class FixedAuthority(PermissionScope answer) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(answer);
    }

    private sealed class SequencedAuthority(params PermissionScope[] answers) : IErpAuthorityRevalidationService
    {
        private int index;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(answers[Math.Min(index++, answers.Length - 1)]);
    }

    private sealed class MemoryStore : IApprovedAccountingSourceCatalogStoreV1, IAccountingCommandLog
    {
        private readonly List<AccountingCommandRecord> commands = [];

        public MemoryStore()
        {
        }

        public MemoryStore(ApprovedAccountingSourcePackagePreviewV1 preview)
        {
            Record = CatalogRecord(preview, ApprovedAccountingSourceCatalogStatusV1.Prepared, 1, null, null, null);
        }

        public ApprovedAccountingSourceCatalogRecordV1? Record { get; private set; }
        public int Prepares { get; private set; }
        public int Activations { get; private set; }
        public Action? AfterFind { get; init; }
        public Action<int>? AfterFindCall { get; set; }
        public Action? AfterRead { get; init; }
        public Action? AfterRecord { get; init; }
        public bool ThrowConcurrentWinner { get; init; }
        private int findCalls;

        public Task<ApprovedAccountingSourceCatalogRecordV1?> ReadAsync(
            Guid packageId,
            CancellationToken cancellationToken = default)
        {
            AfterRead?.Invoke();
            return Task.FromResult(Record);
        }

        public async Task<(ApprovedAccountingSourceCatalogRecordV1 Record, AccountingWriteOutcome Outcome)> PrepareAsync(
            Guid packageId,
            ApprovedAccountingSourcePackagePreviewV1 preview,
            Guid preparedByUserId,
            DateTimeOffset preparedAtUtc,
            Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            await freshAuthorityFence(cancellationToken);
            Prepares++;
            Record = CatalogRecord(preview, ApprovedAccountingSourceCatalogStatusV1.Prepared, 1, null, null, null);
            Complete(metadata, Record);
            if (ThrowConcurrentWinner)
            {
                throw new AccountingStoreException(AccountingStoreCodes.ConcurrentChange, "Synthetic winner.");
            }
            return (Record, AccountingWriteOutcome.Recorded);
        }

        public async Task<ApprovedAccountingSourceCatalogRecordV1> ActivateAsync(
            Guid packageId,
            long expectedPackageVersion,
            long expectedActiveCatalogVersion,
            Guid activatedByUserId,
            DateTimeOffset activatedAtUtc,
            Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            await freshAuthorityFence(cancellationToken);
            await Task.Yield();
            await freshAuthorityFence(cancellationToken);
            Activations++;
            Record = (Record ?? throw new InvalidOperationException()) with
            {
                Status = ApprovedAccountingSourceCatalogStatusV1.Active,
                Version = 2,
                CatalogVersion = 1,
                ActivatedByUserId = activatedByUserId,
                ActivatedAtUtc = activatedAtUtc,
            };
            Complete(metadata, Record);
            return Record;
        }

        public Task<NormalJournalPostingRuleV1?> ResolveAsync(
            Guid companyId,
            Guid sourceId,
            string sourceKind,
            string sourceVersion,
            string ownerProfileId,
            string ownerProfileVersion,
            Guid currencyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NormalJournalPostingRuleV1?>(null);

        public Task<AccountingCommandRecord?> FindCommandAsync(
            string commandName,
            string idempotencyKeyHash,
            CancellationToken cancellationToken = default)
        {
            findCalls++;
            AfterFind?.Invoke();
            AfterFindCall?.Invoke(findCalls);
            return Task.FromResult(commands.SingleOrDefault(command => command.CommandName == commandName
                && command.IdempotencyKeyHash == idempotencyKeyHash));
        }

        public Task RecordCommandAsync(
            AccountingCommandRecord record,
            CancellationToken cancellationToken = default)
        {
            commands.Add(record);
            AfterRecord?.Invoke();
            return Task.CompletedTask;
        }

        private void Complete(AccountingCommandMetadata metadata, ApprovedAccountingSourceCatalogRecordV1 record)
        {
            if (metadata.Completion?.Invoke(record) is { } command)
            {
                commands.Add(command);
            }
        }

        private static ApprovedAccountingSourceCatalogRecordV1 CatalogRecord(
            ApprovedAccountingSourcePackagePreviewV1 preview,
            ApprovedAccountingSourceCatalogStatusV1 status,
            long version,
            long? catalogVersion,
            Guid? activatedBy,
            DateTimeOffset? activatedAt) =>
            new(
                Package,
                Company,
                status,
                version,
                catalogVersion,
                preview.TemplateVersion,
                preview.Package!.Mirror.SourceVersion,
                preview.AccountsSourceSha256,
                preview.PostingMappingsSourceSha256,
                Actor,
                Now,
                activatedBy,
                activatedAt,
                preview.Package);
    }

    private sealed class FixedCompanyContext : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId => Company;
        public void Set(Guid companyId) => throw new NotSupportedException();
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid nextCompanyId) => CompanyId = nextCompanyId;
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class MultiCompanyUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company, OtherCompany]);
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "synthetic-accounting-source-catalog";
        public string? CausationId => null;
        public string SourceChannel => "UnitTest";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
