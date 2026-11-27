using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class NormalJournalDraftLifecycleV1Tests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("nonrelational")] [InlineData("dirty")] [InlineData("pending")]
    [InlineData("ambient")] [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Production_normal_approval_boundary_refuses_unsupported_or_caller_owned_state_without_cleanup(string fault)
    {
        var company = new ApprovalCompany(Company);
        await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var collector = new ApprovalCollector(); var writer = new ApprovalEntryWriter(collector);
        var core = new AccountingStore(db, company, collector, TimeProvider.System,
            new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2)), normalApprovalAuditWriter: fault == "missing-writer" ? null : writer);
        var caller = new AccountingCommandRow { CompanyId = Company, CommandName = "SENTETIK caller", IdempotencyKeyHash = "key", ActorId = "actor", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
        if (fault == "dirty") db.Commands.Add(caller);
        if (fault == "pending") collector.Enqueue(ApprovalCallerAudit());
        using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        var calls = 0;
        var result = await core.WithNormalDraftApprovalSafety().RunNormalApprovalAsync(_ => { calls++; throw new InvalidOperationException("No native mutation"); }, _ =>
        {
            if (fault == "await-dirty") db.Commands.Add(caller);
            if (fault == "await-pending") collector.Enqueue(ApprovalCallerAudit());
            return Task.CompletedTask;
        });
        Assert.Equal(AccountingApiStatus.Unavailable, result.Status); Assert.Equal(0, calls); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.Equal(fault is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
        Assert.Equal(fault is "pending" or "await-pending", writer.HasPendingDrafts);
        if (db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Theory] [InlineData("permission")] [InlineData("actor")] [InlineData("company")] [InlineData("member")] [InlineData("cancel")]
    public async Task Native_original_replay_cannot_release_after_its_command_read_loses_captured_authority(string fault)
    {
        var store = new MemoryStore(Record()); var company = new ApprovalCompany(Company); var user = new ApprovalUser();
        var authority = new ApprovalAuthority(); var log = new ApprovalLog(store);
        var service = ApprovalService(store, log, company, user, authority); var period = Guid.NewGuid();
        var original = await service.ApproveAsync(store.Record.Id, 1, period, "original-approval"); Assert.Equal(AccountingApiStatus.Ok, original.Status);
        using var cancellation = new CancellationTokenSource(); log.AfterRead = () =>
        {
            if (fault == "permission") authority.Allowed = false;
            if (fault == "actor") user.ActorId = Guid.NewGuid();
            if (fault == "company") company.Set(Guid.NewGuid());
            if (fault == "member") user.Member = false;
            if (fault == "cancel") cancellation.Cancel();
        };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApproveAsync(store.Record.Id, 1, period, "original-approval", cancellation.Token));
        else { var result = await service.ApproveAsync(store.Record.Id, 1, period, "original-approval"); Assert.Equal(AccountingApiStatus.Denied, result.Status); Assert.Null(result.Resource); }
        Assert.Equal(1, store.Approvals); Assert.Equal(0, log.Records);
    }

    [Fact]
    public async Task Unsupported_approval_port_is_fail_closed_while_native_argument_validation_still_precedes_it()
    {
        var store = new UnsupportedApprovalPort(); var company = new ApprovalCompany(Company); var user = new ApprovalUser(); var authority = new ApprovalAuthority();
        var service = ApprovalService(store, store, company, user, authority);
        Assert.Equal(AccountingApiStatus.Unavailable, (await service.ApproveAsync(Guid.NewGuid(), 1, Guid.NewGuid(), "valid")).Status);
        Assert.Equal(AccountingApiStatus.Invalid, (await service.ApproveAsync(Guid.Empty, 1, Guid.NewGuid(), "valid")).Status);
        Assert.Equal(AccountingApiStatus.Invalid, (await service.ApproveAsync(Guid.NewGuid(), 1, Guid.NewGuid(), " ")).Status);
        Assert.Equal(0, store.Reads);
    }

    private static NormalJournalDraftServiceV1 ApprovalService(IAccountingNormalJournalDraftStore drafts, IAccountingCommandLog log,
        ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority)
    {
        var binding = new NormalJournalPermissionBindingV1();
        return new(drafts, log, new(binding, authority, company, user), binding, authority, company, user, new FixedCorrelation());
    }
    private sealed class ApprovalCompany(Guid id) : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } = id; public void Set(Guid value) => CompanyId = value; }
    private sealed class ApprovalUser : ICurrentUser
    { public Guid ActorId = Actor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => ActorId.ToString("D");
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class ApprovalAuthority : IErpAuthorityRevalidationService
    { public bool Allowed = true; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) => Task.FromResult(Allowed ? Granted() : PermissionScope.Denied); }
    private sealed class ApprovalLog(IAccountingCommandLog inner) : IAccountingCommandLog
    { public Action? AfterRead; public int Records;
      public async Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default)
      { var result = await inner.FindCommandAsync(command, key, token); AfterRead?.Invoke(); return result; }
      public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken token = default) { Records++; return inner.RecordCommandAsync(record, token); } }
    private sealed class ApprovalCollector : IAuditEventCollector
    { public List<AuditEventDraft> Entries { get; } = []; public void Enqueue(AuditEventDraft draft) => Entries.Add(draft); }
    private sealed class ApprovalEntryWriter(ApprovalCollector collector) : IAuditTransactionWriterV1
    { public int Writes, Discards; public bool HasPendingDrafts => collector.Entries.Count != 0;
      public Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default) { Writes++; throw new InvalidOperationException("Entry-only fixture cannot write Audit"); }
      public void DiscardPending() { Discards++; collector.Entries.Clear(); } }
    private static AuditEventDraft ApprovalCallerAudit() => new(Company, "SENTETIK caller", "Accounting.NormalJournalDraft", "caller", "Approve", 1, 2,
        "User", Actor.ToString("D"), NormalJournalPermissionNamesV1.DraftApprove, [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None, null, "UnitTest", null, null, "SENTETIK-caller", null, "SENTETIK-key");
    private sealed class UnsupportedApprovalPort : IAccountingNormalJournalDraftStore, IAccountingCommandLog
    {
        public int Reads;
        public Task<AccountingCommandRecord?> FindCommandAsync(string c, string k, CancellationToken token = default) { Reads++; throw new NotSupportedException(); }
        public Task RecordCommandAsync(AccountingCommandRecord r, CancellationToken token = default) => throw new NotSupportedException();
        public Task<NormalJournalDraftRecordV1?> FindNormalDraftAsync(Guid id, CancellationToken token = default) { Reads++; throw new NotSupportedException(); }
        public Task<(NormalJournalDraftRecordV1 Record, AccountingWriteOutcome Outcome)> CreateNormalDraftAsync(NormalJournalDraftV1 d, Func<CancellationToken, Task> f, AccountingCommandMetadata m, CancellationToken token = default) => throw new NotSupportedException();
        public Task<NormalJournalDraftRecordV1> ApproveAndPostNormalDraftAsync(Guid id, long version, Guid period, Guid actor, Func<CancellationToken, Task> f, AccountingCommandMetadata m, CancellationToken token = default) => throw new NotSupportedException();
    }
}
