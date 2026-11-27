using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingJournalReversalLifecycleV1Tests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("nonrelational")] [InlineData("dirty")]
    [InlineData("pending")] [InlineData("ambient")] [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Production_reversal_boundary_refuses_unsupported_and_caller_owned_work_without_cleanup(string fault)
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var collector = new ReversalEntryCollector(); var writer = new ReversalEntryWriter(collector);
        var core = new AccountingStore(db, company, collector, TimeProvider.System,
            new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2)),
            normalApprovalAuditWriter: fault == "missing-writer" ? null : writer);
        var caller = new AccountingCommandRow { CompanyId = Company, CommandName = "SENTETIK caller", IdempotencyKeyHash = "key",
            ActorId = "actor", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
        if (fault == "dirty") db.Commands.Add(caller);
        if (fault == "pending") collector.Enqueue(ReversalCallerAudit());
        using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        var mutations = 0;
        var answer = await core.RunApprovalAsync(_ => { mutations++; throw new InvalidOperationException("No mutation"); }, _ =>
        {
            if (fault == "await-dirty") db.Commands.Add(caller);
            if (fault == "await-pending") collector.Enqueue(ReversalCallerAudit());
            return Task.CompletedTask;
        });
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Equal(0, mutations);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.Equal(fault is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
        Assert.Equal(fault is "pending" or "await-pending", writer.HasPendingDrafts);
        if (db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public void Native_reversal_public_contract_remains_sixteen_resource_and_eight_envelope_fields()
    {
        var view = new AccountingJournalReversalProposalViewV1(Proposal, Company, Guid.NewGuid(), null, Guid.NewGuid(),
            new(2026, 9, 25), new(2026, 9, 25), null, "SENTETIK", Maker, Now, "Pending", null, null, null, 1);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(new AccountingApiResult<AccountingJournalReversalProposalViewV1>(AccountingApiStatus.Ok, view, false, null));
        Assert.Equal(16, json.GetProperty("Resource").EnumerateObject().Count());
        Assert.Equal(8, json.EnumerateObject().Count());
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Original_native_approval_replay_rechecks_authority_and_cancellation_after_command_read(bool cancel)
    {
        var store = new MemoryStore(Pending()); var log = new ReversalReadLog(store); var authority = new ReversalReadAuthority();
        var company = new CompanyContext(); company.Set(Company);
        var service = new AccountingJournalReversalServiceV1(store, log, authority, company,
            new FixedUser(Checker), new FixedCorrelation(), new FixedTimeProvider(Now));
        var original = await service.ApproveAsync(Proposal, 1, "reversal-read-fence"); Assert.Equal(AccountingApiStatus.Ok, original.Status);
        using var cancellation = new CancellationTokenSource();
        log.AfterRead = () => { if (cancel) cancellation.Cancel(); else authority.Allowed = false; };
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApproveAsync(Proposal, 1, "reversal-read-fence", cancellation.Token));
        else { var answer = await service.ApproveAsync(Proposal, 1, "reversal-read-fence"); Assert.Equal(AccountingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource); }
        Assert.Equal(1, store.ReversalsWritten); Assert.Equal(1, store.CommandsRecorded); Assert.Equal(0, log.Records);
    }
    private sealed class ReversalReadAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
            => Task.FromResult(Allowed ? Granted() : PermissionScope.Denied);
    }
    private sealed class ReversalReadLog(IAccountingCommandLog inner) : IAccountingCommandLog
    {
        public Action? AfterRead; public int Records;
        public async Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default)
        { var found = await inner.FindCommandAsync(command, key, token); AfterRead?.Invoke(); return found; }
        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken token = default)
        { Records++; return inner.RecordCommandAsync(record, token); }
    }

    private sealed class ReversalEntryCollector : IAuditEventCollector
    { public List<AuditEventDraft> Entries { get; } = []; public void Enqueue(AuditEventDraft draft) => Entries.Add(draft); }
    private sealed class ReversalEntryWriter(ReversalEntryCollector collector) : IAuditTransactionWriterV1
    {
        public int Writes, Discards; public bool HasPendingDrafts => collector.Entries.Count != 0;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default)
        { Writes++; throw new InvalidOperationException("Entry-only fixture cannot write Audit"); }
        public void DiscardPending() { Discards++; collector.Entries.Clear(); }
    }
    private static AuditEventDraft ReversalCallerAudit() => new(Company, "SENTETIK caller", "Accounting.JournalReversal", "caller", "Approve", 1, 2,
        "User", Checker.ToString("D"), AccountingJournalReversalPermissionNamesV1.Approve, [], "{}", "{}", "SENTETIK", null,
        AuditAiInvolvement.None, null, "UnitTest", null, null, "caller", null, "caller-key");
}
