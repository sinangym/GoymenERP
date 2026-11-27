using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingPostingAccountMappingLifecycleV1Tests
{
    [Theory]
    [InlineData("missing-owner")] [InlineData("missing-writer")] [InlineData("other-context")] [InlineData("other-collector")]
    [InlineData("nonrelational")] [InlineData("dirty")] [InlineData("pending")] [InlineData("ambient")]
    [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Mapping_activation_public_boundary_preserves_unsupported_or_caller_owned_state(string fault)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        await using var other = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var collector = new MappingEntryCollector(); var writer = new MappingEntryWriter(collector);
        var core = new AccountingStore(fault == "other-context" ? other : db, company,
            fault == "other-collector" ? new MappingEntryCollector() : collector, TimeProvider.System, null!,
            normalApprovalAuditWriter: fault == "missing-writer" ? null : writer);
        var catalog = new ApprovedAccountingSourceCatalogStoreV1(db, company, collector, TimeProvider.System);
        var store = new AccountingPostingAccountMappingStoreV1(db, catalog, company, collector, TimeProvider.System,
            fault == "missing-owner" ? null : core);
        var caller = new AccountingCommandRow { CompanyId = company.CompanyId, CommandName = "SENTETIK caller", IdempotencyKeyHash = "caller", ActorId = "actor", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
        if (fault == "dirty") db.Commands.Add(caller);
        if (fault == "pending") collector.Enqueue(MappingEntryAudit(company.CompanyId));
        using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        var attempts = 0;
        var answer = await store.RunActivationAsync(_ => { attempts++; throw new InvalidOperationException("Entry cannot reach native reads"); }, _ =>
        {
            if (fault == "await-dirty") db.Commands.Add(caller);
            if (fault == "await-pending") collector.Enqueue(MappingEntryAudit(company.CompanyId));
            return Task.CompletedTask;
        });
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Equal(0, attempts);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.Equal(fault is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
        Assert.Equal(fault is "pending" or "await-pending", writer.HasPendingDrafts);
        if (db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Default_mapping_port_does_not_adopt_an_unguarded_activation_attempt()
    {
        IAccountingPostingAccountMappingStoreV1 port = new FailingStore(); var calls = 0;
        var result = await port.RunActivationAsync(_ => { calls++; throw new InvalidOperationException(); }, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable, result.Status); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Cancelled_mapping_entry_does_not_read_or_write_even_when_the_fence_ignores_its_token()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var collector = new MappingEntryCollector(); var writer = new MappingEntryWriter(collector);
        var core = new AccountingStore(db, company, collector, TimeProvider.System, null!, normalApprovalAuditWriter: writer);
        var store = new AccountingPostingAccountMappingStoreV1(db, new(db, company, collector, TimeProvider.System), company, collector, TimeProvider.System, core);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RunActivationAsync(_ => { calls++; throw new InvalidOperationException(); }, _ => { calls++; return Task.CompletedTask; }, cancellation.Token));
        Assert.Equal(0, calls); Assert.Equal(0, writer.Discards); Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")] [InlineData("cancel")]
    public async Task Mapping_activation_captures_context_before_first_authority_await_and_preserves_entry_on_drift(string fault)
    {
        var companyId = Guid.NewGuid(); var company = new MappingEntryCompany(companyId); var user = new MappingEntryUser(companyId);
        await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var collector = new MappingEntryCollector(); var writer = new MappingEntryWriter(collector);
        var core = new AccountingStore(db, company, collector, TimeProvider.System, null!, normalApprovalAuditWriter: writer);
        var store = new AccountingPostingAccountMappingStoreV1(db, new(db, company, collector, TimeProvider.System), company, collector, TimeProvider.System, core);
        using var cancellation = new CancellationTokenSource(); var calls = 0;
        var authority = new MappingEntryAuthority(() =>
        {
            calls++;
            if (fault == "company") company.Set(Guid.NewGuid());
            if (fault == "actor") user.Actor = Checker;
            if (fault == "member") user.Member = false;
            if (fault == "cancel") cancellation.Cancel();
            return fault != "permission";
        });
        var service = new AccountingPostingAccountMappingServiceV1(store, core, authority, company, user, new FixedCorrelation("SENTETIK-captured"), TimeProvider.System);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ActivateAsync(1, new(1, 0), "capture", cancellation.Token));
        else
        {
            var result = await service.ActivateAsync(1, new(1, 0), "capture");
            Assert.Equal(AccountingApiStatus.Denied, result.Status); Assert.Equal(AccountingApiCodes.AuthorityDenied, result.Code); Assert.Null(result.Resource);
        }
        Assert.Equal(1, calls); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.False(db.ChangeTracker.HasChanges()); Assert.Null(db.Database.CurrentTransaction);
    }

    private sealed class MappingEntryCompany(Guid company) : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } = company; public void Set(Guid value) => CompanyId = value; }
    private sealed class MappingEntryUser(Guid company) : ICurrentUser
    {
        public Guid Actor = Preparer; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company }; public bool CanAccessCompany(Guid value) => Member && value == company;
    }
    private sealed class MappingEntryAuthority(Func<bool> read) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) => Task.FromResult(Scope(read()));
    }

    // The existing in-memory Book remains an explicit algorithm/hash/refusal protocol fixture.
    // Production uses the store's fail-closed owned boundary; no relational-success claim is made here.
    private sealed class SyntheticMappingActivationProtocolStore(IAccountingPostingAccountMappingStoreV1 inner) : IAccountingPostingAccountMappingStoreV1
    {
        public async Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> RunActivationAsync(Func<bool, Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>>> attempt,
            Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); var answer = await attempt(false); await fence(token); return answer; }
        public Task<AccountingPostingAccountMappingRecordV1?> ReadAsync(long version, CancellationToken token = default) => inner.ReadAsync(version, token);
        public Task<AccountingPostingAccountMappingRecordV1?> ReadActiveAsync(CancellationToken token = default) => inner.ReadActiveAsync(token);
        public Task<AccountingPostingAccountMappingRecordV1> PrepareAsync(long latest, DateOnly from, IReadOnlyList<AccountingPostingAccountMappingDraftRowV1> rows, Guid actor,
            DateTimeOffset at, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => inner.PrepareAsync(latest, from, rows, actor, at, fence, metadata, token);
        public Task<AccountingPostingAccountMappingRecordV1> ActivateAsync(long version, long expected, long active, Guid actor, DateTimeOffset at,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => inner.ActivateAsync(version, expected, active, actor, at, fence, metadata, token);
    }
    private sealed class MappingEntryCollector : IAuditEventCollector
    { public List<AuditEventDraft> Entries { get; } = []; public void Enqueue(AuditEventDraft draft) => Entries.Add(draft); }
    private sealed class MappingEntryWriter(MappingEntryCollector collector) : IAuditTransactionWriterV1
    {
        public int Writes, Discards; public bool HasPendingDrafts => collector.Entries.Count != 0;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default) { Writes++; throw new InvalidOperationException("Entry-only fixture cannot write Audit"); }
        public void DiscardPending() { Discards++; collector.Entries.Clear(); }
    }
    private static AuditEventDraft MappingEntryAudit(Guid company) => new(company, "SENTETIK caller", "Accounting.PostingAccountMapping", "caller", "Activate", 1, 2,
        "User", Preparer.ToString("D"), AccountingPostingAccountMappingPermissionNamesV1.Activate, [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None,
        null, "UnitTest", null, null, "SENTETIK-caller", null, "SENTETIK-key");
}
