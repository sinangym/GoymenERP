using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// B-04a: the posting account mapping lifecycle through the real service, the real mapping store and the real
/// command log on one in-memory Accounting database per test. What SQL Server enforces itself (the single active
/// version index, the maker-checker check, the Down guard) is proven by the relational acceptance tests.
/// </summary>
public sealed class AccountingPostingAccountMappingLifecycleV1Tests
{
    private static readonly Guid Preparer = Guid.Parse("b0400000-0000-4000-8000-000000000001");
    private static readonly Guid Checker = Guid.Parse("b0400000-0000-4000-8000-000000000002");
    private static readonly Guid Receivable120 = Guid.Parse("b0400000-0000-4000-8000-000000000120");
    private static readonly Guid Revenue600 = Guid.Parse("b0400000-0000-4000-8000-000000000600");
    private static readonly Guid Export601 = Guid.Parse("b0400000-0000-4000-8000-000000000601");
    private static readonly Guid Inventory150 = Guid.Parse("b0400000-0000-4000-8000-000000000150");
    private static readonly Guid Inventory153 = Guid.Parse("b0400000-0000-4000-8000-000000000153");
    private static readonly Guid UnknownAccount = Guid.Parse("b0400000-0000-4000-8000-000000000999");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-07T09:00:00Z");
    private static readonly DateOnly EffectiveFrom = new(2026, 11, 1);

    private static readonly string[] All =
    [
        AccountingPostingAccountMappingPermissionNamesV1.Read,
        AccountingPostingAccountMappingPermissionNamesV1.Prepare,
        AccountingPostingAccountMappingPermissionNamesV1.Activate,
    ];

    [Fact]
    public async Task A_prepared_version_is_activated_by_another_person_and_supersedes_the_previous_active_one()
    {
        var book = await Book.CreateAsync();

        var first = await book.Open(Preparer).Service.PrepareAsync(Request(0), "prepare-1");
        Assert.Equal(AccountingApiStatus.Created, first.Status);
        Assert.Equal(("Prepared", 1L, 1L), (first.Resource!.Status, first.Resource.MappingVersion, first.Resource.Version));
        Assert.Equal(["120.01", "600.01"], first.Resource.Rows.Select(row => row.AccountCode));

        var activated = await book.Open(Checker).Service.ActivateAsync(1, new(1, 0), "activate-1");
        Assert.Equal(AccountingApiStatus.Ok, activated.Status);
        Assert.Equal(("Active", 2L, Checker), (activated.Resource!.Status, activated.Resource.Version,
            activated.Resource.ActivatedByUserId!.Value));

        var second = await book.Open(Preparer).Service.PrepareAsync(Request(1, EffectiveFrom.AddDays(10)), "prepare-2");
        Assert.Equal(2, second.Resource!.MappingVersion);
        var activatedSecond = await book.Open(Checker).Service.ActivateAsync(2, new(1, 1), "activate-2");
        Assert.Equal(AccountingApiStatus.Ok, activatedSecond.Status);

        var old = await book.Open(Checker).Service.ReadAsync(1);
        var active = await book.Open(Checker).Service.ReadActiveAsync();
        Assert.Equal(("Superseded", 3L), (old.Resource!.Status, old.Resource.Version));
        Assert.Equal((2L, "Active"), (active.Resource!.MappingVersion, active.Resource.Status));
        Assert.Equal(1, await book.CountAsync(row => row.Status == 2));
    }

    [Fact]
    public async Task The_preparer_cannot_activate_their_own_version_and_the_recorded_refusal_replays()
    {
        var book = await Book.CreateAsync();
        await book.Open(Preparer).Service.PrepareAsync(Request(0), "prepare");

        var refused = await book.Open(Preparer).Service.ActivateAsync(1, new(1, 0), "self");
        var replay = await book.Open(Preparer).Service.ActivateAsync(1, new(1, 0), "self");

        Assert.Equal((AccountingApiStatus.Denied, AccountingPostingAccountMappingCodesV1.SelfActivation),
            (refused.Status, refused.Code));
        Assert.False(refused.IsReplay);
        Assert.Equal((AccountingApiStatus.Denied, AccountingPostingAccountMappingCodesV1.SelfActivation, true),
            (replay.Status, replay.Code, replay.IsReplay));
        Assert.Equal("Prepared", (await book.Open(Checker).Service.ReadAsync(1)).Resource!.Status);
        Assert.Equal(0, await book.CountAsync(row => row.Status == 2));
    }

    [Fact]
    public async Task Stale_expectations_are_recorded_conflicts_that_the_same_key_replays()
    {
        var book = await Book.CreateAsync();
        await book.Open(Preparer).Service.PrepareAsync(Request(0), "prepare");

        var stalePrepare = await book.Open(Preparer).Service.PrepareAsync(Request(0), "stale-prepare");
        var staleVersion = await book.Open(Checker).Service.ActivateAsync(1, new(2, 0), "stale-version");
        var staleActive = await book.Open(Checker).Service.ActivateAsync(1, new(1, 7), "stale-active");
        var replayed = await book.Open(Checker).Service.ActivateAsync(1, new(1, 7), "stale-active");

        Assert.Equal((AccountingApiStatus.Conflict, AccountingPostingAccountMappingCodesV1.SequenceConflict,
            "expectedLatestMappingVersion", 0L, 1L), (stalePrepare.Status, stalePrepare.Code, stalePrepare.Field,
            stalePrepare.ExpectedVersion!.Value, stalePrepare.CurrentVersion!.Value));
        Assert.Equal((AccountingApiStatus.Conflict, AccountingApiCodes.VersionConflict),
            (staleVersion.Status, staleVersion.Code));
        Assert.Equal((AccountingPostingAccountMappingCodesV1.SequenceConflict, "expectedActiveMappingVersion"),
            (staleActive.Code, staleActive.Field));
        Assert.True(replayed.IsReplay);
        Assert.Equal(staleActive.Code, replayed.Code);
        Assert.Equal(1, await book.CountAsync(_ => true));
    }

    [Fact]
    public async Task The_same_key_with_another_payload_is_refused_and_a_replay_keeps_the_original_correlation()
    {
        var book = await Book.CreateAsync();
        var first = await book.Open(Preparer, correlation: "first-correlation").Service.PrepareAsync(Request(0), "key");
        var replay = await book.Open(Preparer, correlation: "second-correlation").Service.PrepareAsync(Request(0), "key");
        var other = await book.Open(Preparer).Service.PrepareAsync(Request(0, EffectiveFrom.AddDays(1)), "key");
        var otherActor = await book.Open(Checker).Service.PrepareAsync(Request(0), "key");

        Assert.Equal(AccountingApiStatus.Created, first.Status);
        Assert.Equal((AccountingApiStatus.Created, true, "first-correlation"),
            (replay.Status, replay.IsReplay, replay.OriginalCorrelationId!));
        Assert.NotNull(first.Resource);
        Assert.NotNull(replay.Resource);
        Assert.Equal(first.Resource, replay.Resource with { Rows = first.Resource.Rows });
        Assert.Equal(first.Resource.Rows, replay.Resource.Rows);
        Assert.Equal((AccountingApiStatus.Conflict, AccountingApiCodes.IdempotencyConflict), (other.Status, other.Code));
        Assert.Equal((AccountingApiStatus.Conflict, AccountingApiCodes.IdempotencyConflict),
            (otherActor.Status, otherActor.Code));
        Assert.Equal(1, await book.CountAsync(_ => true));
    }

    [Theory]
    [InlineData("empty", AccountingPostingAccountMappingCodesV1.RowsRequired, "rows")]
    [InlineData("purpose", AccountingPostingAccountMappingCodesV1.PurposeUnknown, "rows[0].purpose")]
    [InlineData("key-of-other-purpose", AccountingPostingAccountMappingCodesV1.KeyUnknown, "rows[0].mappingKey")]
    [InlineData("item-type", AccountingPostingAccountMappingCodesV1.SelectorInvalid, "rows[0].itemType")]
    [InlineData("line-purpose", AccountingPostingAccountMappingCodesV1.SelectorInvalid, "rows[0].linePurpose")]
    [InlineData("empty-account", AccountingPostingAccountMappingCodesV1.AccountUnknown, "rows[0].accountId")]
    [InlineData("duplicate", AccountingPostingAccountMappingCodesV1.DuplicateRow, "rows[1]")]
    [InlineData("vat-rate-on-revenue", AccountingPostingAccountMappingCodesV1.SelectorInvalid, "rows[0].vatRateCode")]
    [InlineData("unknown-account", AccountingPostingAccountMappingCodesV1.AccountUnknown, "rows[0].accountId")]
    public async Task Payload_refusals_name_the_field_and_store_no_version(string fault, string code, string field)
    {
        var book = await Book.CreateAsync();
        var sales = AccountingPostingAccountMappingPurposesV1.SalesInvoice;
        AccountingPostingAccountMappingRowRequestV1[] rows = fault switch
        {
            "empty" => [],
            "purpose" => [new("sales", "receivable", null, null, null, Receivable120)],
            "key-of-other-purpose" => [new(sales, "payable", null, null, null, Receivable120)],
            "item-type" => [new(sales, "revenue", " Steel", null, null, Revenue600)],
            "line-purpose" => [new(sales, "revenue", null, "ham madde", null, Revenue600)],
            "empty-account" => [new(sales, "revenue", null, null, null, Guid.Empty)],
            "duplicate" =>
            [
                new(sales, "revenue", "Steel", null, null, Revenue600),
                new(sales, "revenue", "Steel", null, null, Export601),
            ],
            "vat-rate-on-revenue" => [new(sales, "revenue", null, null, null, Revenue600, "KDV20")],
            _ => [new(sales, "revenue", null, null, null, UnknownAccount)],
        };

        var result = await book.Open(Preparer).Service.PrepareAsync(new(0, EffectiveFrom, rows), "fault-" + fault);

        Assert.Equal((AccountingApiStatus.Unprocessable, code, field), (result.Status, result.Code, result.Field));
        Assert.Equal(0, await book.CountAsync(_ => true));
    }

    [Fact]
    public async Task Without_an_active_approved_account_plan_nothing_can_be_prepared()
    {
        var book = await Book.CreateAsync(withPlan: false);

        var result = await book.Open(Preparer).Service.PrepareAsync(Request(0), "no-plan");

        Assert.Equal((AccountingApiStatus.Conflict, AccountingPostingAccountMappingCodesV1.AccountPlanUnavailable),
            (result.Status, result.Code));
        Assert.Equal(0, await book.CountAsync(_ => true));
    }

    [Fact]
    public async Task Missing_authority_or_authority_lost_at_the_store_fence_stores_nothing()
    {
        var book = await Book.CreateAsync();

        var withoutPrepare = await book.Open(Preparer, granted: [AccountingPostingAccountMappingPermissionNamesV1.Read])
            .Service.PrepareAsync(Request(0), "no-grant");
        var lost = await book.Open(Preparer, authority: new LosingAuthority(4)).Service.PrepareAsync(Request(0), "lost");
        var unreadable = await book.Open(Preparer, granted: [AccountingPostingAccountMappingPermissionNamesV1.Prepare])
            .Service.ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Denied, withoutPrepare.Status);
        Assert.Equal(AccountingApiStatus.Denied, lost.Status);
        Assert.Null(lost.Resource);
        Assert.Equal(AccountingApiStatus.Denied, unreadable.Status);
        Assert.Equal(0, await book.CountAsync(_ => true));
    }

    [Fact]
    public async Task Reads_answer_not_found_for_an_unknown_version_or_no_active_version()
    {
        var book = await Book.CreateAsync();

        Assert.Equal(AccountingApiStatus.NotFound, (await book.Open(Checker).Service.ReadActiveAsync()).Status);
        Assert.Equal(AccountingApiStatus.NotFound, (await book.Open(Checker).Service.ReadAsync(4)).Status);
        Assert.Equal((AccountingApiStatus.Invalid, "mappingVersion"),
            ((await book.Open(Checker).Service.ReadAsync(0)).Status, (await book.Open(Checker).Service.ReadAsync(0)).Field));
    }

    [Fact]
    public async Task The_resolver_answers_the_most_specific_active_row_and_never_guesses()
    {
        var book = await Book.CreateAsync();
        var sales = AccountingPostingAccountMappingPurposesV1.SalesInvoice;
        var purchase = AccountingPostingAccountMappingPurposesV1.PurchaseInvoice;
        await using (var scope = book.Open(Preparer))
        {
            var resolver = scope.Resolver;
            Assert.Equal(AccountingPostingAccountMappingUnavailableReasonsV1.NoActiveMapping,
                (await resolver.ResolveAsync(book.Company, sales, EffectiveFrom, "revenue")).UnavailableReason);
        }

        AccountingPostingAccountMappingRowRequestV1[] rows =
        [
            new(sales, "revenue", null, null, null, Revenue600),
            new(sales, "revenue", null, null, true, Export601),
            new(purchase, "inventory", null, "raw-material", null, Inventory150),
            new(purchase, "inventory", "Steel", null, null, Inventory153),
        ];
        Assert.Equal(AccountingApiStatus.Created,
            (await book.Open(Preparer).Service.PrepareAsync(new(0, EffectiveFrom, rows), "p")).Status);
        Assert.Equal(AccountingApiStatus.Ok, (await book.Open(Checker).Service.ActivateAsync(1, new(1, 0), "a")).Status);

        await using var open = book.Open(Preparer);
        var port = open.Resolver;
        var domestic = await port.ResolveAsync(book.Company, sales, EffectiveFrom, "revenue", "Steel", null, false);
        var export = await port.ResolveAsync(book.Company, sales, EffectiveFrom, "revenue", "Steel", null, true);
        var unknownExport = await port.ResolveAsync(book.Company, sales, EffectiveFrom, "revenue");
        var rawSteel = await port.ResolveAsync(book.Company, purchase, EffectiveFrom, "inventory", "Steel", "raw-material");
        var service = await port.ResolveAsync(book.Company, purchase, EffectiveFrom, "inventory", "Service", null);
        var vat = await port.ResolveAsync(book.Company, sales, EffectiveFrom, "vat-output");
        var early = await port.ResolveAsync(book.Company, sales, EffectiveFrom.AddDays(-1), "revenue");
        var foreign = await port.ResolveAsync(Guid.NewGuid(), sales, EffectiveFrom, "revenue");
        var wrongKey = await port.ResolveAsync(book.Company, sales, EffectiveFrom, "payable");

        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Mapped(Revenue600, 1), domestic);
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Mapped(Export601, 1), export);
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Mapped(Revenue600, 1), unknownExport);
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Ambiguous(1), rawSteel);
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Unmapped(1), service);
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Unmapped(1), vat);
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Unavailable(
            AccountingPostingAccountMappingUnavailableReasonsV1.NotEffectiveForDate, 1), early);
        Assert.Equal(AccountingPostingAccountMappingUnavailableReasonsV1.CompanyContextInvalid, foreign.UnavailableReason);
        Assert.Equal(AccountingPostingAccountMappingUnavailableReasonsV1.QueryInvalid, wrongKey.UnavailableReason);
    }

    [Fact]
    public async Task An_unknown_store_answer_is_unavailable_and_records_nothing()
    {
        var context = new CompanyContext();
        context.Set(Guid.NewGuid());
        var commands = new RecordingLog();
        var service = new AccountingPostingAccountMappingServiceV1(
            new FailingStore(), commands, new GrantingAuthority(All), context,
            new FixedUser(Preparer, context.CompanyId), new FixedCorrelation("c"), new FixedTimeProvider(Now));

        var result = await service.PrepareAsync(Request(0), "unavailable");

        Assert.Equal(AccountingApiStatus.Unavailable, result.Status);
        Assert.Equal(0, commands.Records);
    }

    private static PrepareAccountingPostingAccountMappingRequestV1 Request(long expectedLatest, DateOnly? from = null) =>
        new(expectedLatest, from ?? EffectiveFrom,
        [
            new(AccountingPostingAccountMappingPurposesV1.SalesInvoice, AccountingPostingAccountMappingKeysV1.Receivable,
                null, null, null, Receivable120),
            new(AccountingPostingAccountMappingPurposesV1.SalesInvoice, AccountingPostingAccountMappingKeysV1.Revenue,
                null, null, null, Revenue600),
        ]);

    private static PermissionScope Scope(bool granted) => new(
        granted,
        true,
        new HashSet<string>(["SYNTHETIC-B04A"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    private sealed class Book
    {
        private readonly InMemoryDatabaseRoot root = new();
        private readonly string database = Guid.NewGuid().ToString("N");

        public Guid Company { get; } = Guid.NewGuid();

        public static async Task<Book> CreateAsync(bool withPlan = true)
        {
            var book = new Book();
            if (withPlan)
            {
                await book.SeedPlanAsync();
            }

            return book;
        }

        public RequestScope Open(
            Guid actor,
            string[]? granted = null,
            IErpAuthorityRevalidationService? authority = null,
            string? correlation = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(Company);
            var dbContext = NewContext(companyContext);
            var time = new FixedTimeProvider(Now);
            var log = new AccountingStore(dbContext, companyContext, new NullAuditCollector(), time,
                new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2)));
            var catalog = new ApprovedAccountingSourceCatalogStoreV1(
                dbContext, companyContext, new NullAuditCollector(), time);
            var store = new AccountingPostingAccountMappingStoreV1(
                dbContext, catalog, companyContext, new NullAuditCollector(), time);
            var service = new AccountingPostingAccountMappingServiceV1(
                store, log, authority ?? new GrantingAuthority(granted ?? All), companyContext,
                new FixedUser(actor, Company), new FixedCorrelation(correlation ?? Guid.NewGuid().ToString("N")), time);
            return new RequestScope(dbContext, service, store);
        }

        public async Task<int> CountAsync(
            System.Linq.Expressions.Expression<Func<AccountingPostingAccountMappingVersionRow, bool>> predicate)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(Company);
            await using var dbContext = NewContext(companyContext);
            return await dbContext.PostingAccountMappingVersions.CountAsync(predicate);
        }

        private AccountingDbContext NewContext(CompanyContext companyContext) =>
            new(new DbContextOptionsBuilder<AccountingDbContext>()
                .UseInMemoryDatabase(database, root)
                .ConfigureWarnings(warnings => warnings.Ignore(
                    InMemoryEventId.TransactionIgnoredWarning, CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options, companyContext);

        private async Task SeedPlanAsync()
        {
            var companyContext = new CompanyContext();
            companyContext.Set(Company);
            await using var dbContext = NewContext(companyContext);
            var time = new FixedTimeProvider(Now);
            var log = new AccountingStore(dbContext, companyContext, new NullAuditCollector(), time,
                new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2)));
            var catalog = new ApprovedAccountingSourceCatalogStoreV1(
                dbContext, companyContext, new NullAuditCollector(), time);
            var service = new ApprovedAccountingSourceCatalogServiceV1(
                new ApprovedAccountingSourcePackageParserV1(), catalog, log,
                new GrantingAuthority([ApprovedAccountingSourceCatalogPermissionNamesV1.Prepare,
                    ApprovedAccountingSourceCatalogPermissionNamesV1.Activate]),
                companyContext, new FixedUser(Preparer, Company), new FixedCorrelation("seed"), time);
            var accounts = Encoding.UTF8.GetBytes(
                ApprovedAccountingSourcePackageTemplateV1.AccountsHeader + "\n"
                + $"{Receivable120:D},120.01,SENTETIK alicilar\n"
                + $"{Revenue600:D},600.01,SENTETIK yurt ici satislar\n"
                + $"{Export601:D},601.01,SENTETIK yurt disi satislar\n"
                + $"{Inventory150:D},150.01,SENTETIK ilk madde\n"
                + $"{Inventory153:D},153.01,SENTETIK ticari mallar\n");
            var mappings = Encoding.UTF8.GetBytes(
                ApprovedAccountingSourcePackageTemplateV1.MappingsHeader + "\n"
                + "30000000-0000-4000-8000-000000000001,finance.manual-receipt.recorded.v1,v1,profile,profile-v1,"
                + $"40000000-0000-4000-8000-000000000001,receipt,rule-v1,debit,Debit,{Receivable120:D}\n"
                + "30000000-0000-4000-8000-000000000001,finance.manual-receipt.recorded.v1,v1,profile,profile-v1,"
                + $"40000000-0000-4000-8000-000000000001,receipt,rule-v1,credit,Credit,{Revenue600:D}\n");
            var packageId = Guid.NewGuid();
            var prepared = await service.PrepareAsync(new PrepareApprovedAccountingSourceCatalogRequestV1(
                packageId, "mikro-synthetic-b04a", accounts, mappings,
                Convert.ToHexString(SHA256.HashData(accounts)), Convert.ToHexString(SHA256.HashData(mappings))),
                "seed-prepare");
            Assert.Equal(AccountingApiStatus.Created, prepared.Status);
            var activated = await service.ActivateAsync(packageId,
                new ActivateApprovedAccountingSourceCatalogRequestV1(1, 0), "seed-activate");
            Assert.Equal(AccountingApiStatus.Ok, activated.Status);
        }
    }

    private sealed class RequestScope(
        AccountingDbContext dbContext,
        AccountingPostingAccountMappingServiceV1 service,
        AccountingPostingAccountMappingStoreV1 store) : IAsyncDisposable
    {
        public AccountingPostingAccountMappingServiceV1 Service { get; } = service;

        public IAccountingPostingAccountMappingResolverV1 Resolver { get; } = store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class GrantingAuthority(string[] granted) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(granted.Contains(permission, StringComparer.Ordinal)));
    }

    /// <summary>Grants every call until the given call, which (and every later one) is denied.</summary>
    private sealed class LosingAuthority(int deniedFromCall) : IErpAuthorityRevalidationService
    {
        private int calls;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(Interlocked.Increment(ref calls) < deniedFromCall));
    }

    private sealed class FailingStore : IAccountingPostingAccountMappingStoreV1
    {
        public Task<AccountingPostingAccountMappingRecordV1?> ReadAsync(long mappingVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AccountingPostingAccountMappingRecordV1?> ReadActiveAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AccountingPostingAccountMappingRecordV1> PrepareAsync(long expectedLatestMappingVersion,
            DateOnly effectiveFrom, IReadOnlyList<AccountingPostingAccountMappingDraftRowV1> rows, Guid preparedByUserId,
            DateTimeOffset preparedAtUtc, Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new AccountingStoreException("synthetic_unknown_store_answer", "Synthetic.");

        public Task<AccountingPostingAccountMappingRecordV1> ActivateAsync(long mappingVersion, long expectedVersion,
            long expectedActiveMappingVersion, Guid activatedByUserId, DateTimeOffset activatedAtUtc,
            Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingLog : IAccountingCommandLog
    {
        public int Records { get; private set; }

        public Task<AccountingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
            CancellationToken cancellationToken = default) => Task.FromResult<AccountingCommandRecord?>(null);

        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken cancellationToken = default)
        {
            Records++;
            return Task.CompletedTask;
        }
    }

    private sealed class NullAuditCollector : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent)
        {
        }
    }

    private sealed class FixedUser(Guid actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => null;
        public string SourceChannel => "UnitTest";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
            string? userAgent) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
