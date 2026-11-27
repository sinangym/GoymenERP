using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class ApprovedAccountingSourceCatalogLifecycleV1Tests
{
    [Theory]
    [InlineData("missing-owner")] [InlineData("missing-writer")] [InlineData("other-context")] [InlineData("other-collector")]
    [InlineData("nonrelational")] [InlineData("dirty")] [InlineData("pending")] [InlineData("ambient")]
    [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Production_catalog_activation_refuses_unsupported_or_caller_owned_state_without_clearing_it(string fault)
    {
        var company = new FixedCompanyContext();
        await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        await using var otherDb = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var collector = new ActivationCollector(); var writer = new ActivationEntryWriter(collector);
        // Entry-only negative fixture never executes amount verification or fabricates SQL success.
        var core = new AccountingStore(fault == "other-context" ? otherDb : db, company,
            fault == "other-collector" ? new ActivationCollector() : collector, TimeProvider.System, null!,
            normalApprovalAuditWriter: fault == "missing-writer" ? null : writer);
        var store = new ApprovedAccountingSourceCatalogStoreV1(db, company, collector, TimeProvider.System,
            fault == "missing-owner" ? null : core);
        var caller = new AccountingCommandRow { CompanyId = Company, CommandName = "SENTETIK caller", IdempotencyKeyHash = "caller-key", ActorId = "actor", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
        if (fault == "dirty") db.Commands.Add(caller);
        if (fault == "pending") collector.Enqueue(ActivationCallerAudit());
        using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        var attempts = 0;
        var answer = await store.RunActivationAsync(_ => { attempts++; throw new InvalidOperationException("Entry must not reach native reads"); }, _ =>
        {
            if (fault == "await-dirty") db.Commands.Add(caller);
            if (fault == "await-pending") collector.Enqueue(ActivationCallerAudit());
            return Task.CompletedTask;
        });
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Equal(0, attempts);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.Equal(fault is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
        Assert.Equal(fault is "pending" or "await-pending", writer.HasPendingDrafts);
        if (db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Synthetic_catalog_original_replay_rechecks_captured_authority_after_command_read(string fault)
    {
        var company = new MutableCompanyContext(Company); var user = new ActivationUser(); var authority = new ActivationAuthority();
        var armed = false; using var cancellation = new CancellationTokenSource();
        var store = new MemoryStore(await Preview()) { AfterFind = () =>
        {
            if (!armed) return;
            if (fault == "company") company.Set(OtherCompany);
            if (fault == "actor") user.ActorId = Guid.NewGuid();
            if (fault == "member") user.Member = false;
            if (fault == "permission") authority.Allowed = false;
            if (fault == "cancel") cancellation.Cancel();
        } };
        var service = Service(store, authority, company, user);
        var request = new ActivateApprovedAccountingSourceCatalogRequestV1(1, 0);
        Assert.Equal(AccountingApiStatus.Ok, (await service.ActivateAsync(Package, request, "original-catalog")).Status);
        armed = true;
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ActivateAsync(Package, request, "original-catalog", cancellation.Token));
        else
        {
            var answer = await service.ActivateAsync(Package, request, "original-catalog");
            Assert.Equal(AccountingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource); Assert.False(answer.IsReplay);
        }
        Assert.Equal(1, store.Activations); Assert.Equal(1, store.CommandCount);
    }

    private sealed class ActivationUser : ICurrentUser
    {
        public Guid ActorId = Actor; public bool Member = true; public bool IsAuthenticated => true;
        public string UserId => ActorId.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => Member && companyId == Company;
    }
    private sealed class ActivationAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Allowed ? Granted() : PermissionScope.Denied);
    }
    private sealed class ActivationCollector : IAuditEventCollector
    { public List<AuditEventDraft> Entries { get; } = []; public void Enqueue(AuditEventDraft draft) => Entries.Add(draft); }
    private sealed class ActivationEntryWriter(ActivationCollector collector) : IAuditTransactionWriterV1
    {
        public int Writes, Discards; public bool HasPendingDrafts => collector.Entries.Count != 0;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default) { Writes++; throw new InvalidOperationException("Entry-only fixture cannot write Audit"); }
        public void DiscardPending() { Discards++; collector.Entries.Clear(); }
    }
    private static AuditEventDraft ActivationCallerAudit() => new(Company, "SENTETIK caller", "Accounting.ApprovedSourceCatalog", "caller", "Activate", 1, 2,
        "User", Actor.ToString("D"), ApprovedAccountingSourceCatalogPermissionNamesV1.Activate, [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None,
        null, "UnitTest", null, null, "SENTETIK-caller", null, "SENTETIK-key");
}
