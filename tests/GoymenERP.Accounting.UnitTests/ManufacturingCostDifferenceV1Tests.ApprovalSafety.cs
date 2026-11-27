using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class ManufacturingCostDifferenceV1Tests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("other-context")] [InlineData("other-collector")]
    [InlineData("nonrelational")] [InlineData("dirty")] [InlineData("pending")] [InlineData("ambient")]
    [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Production_cost_difference_boundary_preserves_caller_work_and_never_claims_InMemory_transaction_success(string fault)
    {
        var company = new CostCompany();
        await using var db = CostDb(company); await using var other = CostDb(company);
        var collector = new CostCollector(); var writer = new CostEntryWriter(collector);
        var posting = new AccountingStore(fault == "other-context" ? other : db, company,
            fault == "other-collector" ? new CostCollector() : collector, TimeProvider.System, null!,
            normalApprovalAuditWriter: fault == "missing-writer" ? null : writer);
        var store = new ManufacturingCostDifferenceStoreV1(db, posting, company, null!, null!, collector, TimeProvider.System);
        var caller = CostCaller();
        if (fault == "dirty") db.Commands.Add(caller);
        if (fault == "pending") collector.Enqueue(CostCallerAudit());
        using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        var attempts = 0;
        var answer = await store.RunApprovalAsync(_ => { attempts++; throw new InvalidOperationException("No native read may run"); }, _ =>
        {
            if (fault == "await-dirty") db.Commands.Add(caller);
            if (fault == "await-pending") collector.Enqueue(CostCallerAudit());
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
    public async Task Legacy_unimplemented_approval_boundary_fails_closed_before_calling_the_native_delegate()
    {
        IManufacturingCostDifferenceStoreV1 store = new Store(); var attempts = 0;
        var answer = await store.RunApprovalAsync(_ => { attempts++; throw new InvalidOperationException(); }, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Equal(0, attempts);
    }

    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")] [InlineData("scope")] [InlineData("null-scope")] [InlineData("cancel")]
    public async Task Production_cost_approval_captures_context_before_first_await_and_keeps_caller_drafts_on_denial(string fault)
    {
        var company = new CostCompany(); var user = new CostUser();
        await using var db = CostDb(company); var collector = new CostCollector(); collector.Enqueue(CostCallerAudit()); var writer = new CostEntryWriter(collector);
        var core = new AccountingStore(db, company, collector, TimeProvider.System, null!, normalApprovalAuditWriter: writer);
        var store = new ManufacturingCostDifferenceStoreV1(db, core, company, null!, null!, collector, TimeProvider.System);
        var authority = new CostAuthority(); using var cancellation = new CancellationTokenSource();
        authority.AfterRead = () =>
        {
            if (fault == "company") company.Set(Guid.NewGuid()); if (fault == "actor") user.Actor = Maker;
            if (fault == "member") user.Member = false; if (fault == "permission") authority.Granted = false;
            if (fault == "scope") authority.CompanyWide = false; if (fault == "null-scope") authority.Null = true;
            if (fault == "cancel") cancellation.Cancel();
        };
        var service = new ManufacturingCostDifferenceServiceV1(store, core, authority, company, user, new Correlation());
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApproveAsync(EventId, 2, "capture", cancellation.Token));
        else Assert.Equal(AccountingApiStatus.Denied, (await service.ApproveAsync(EventId, 2, "capture")).Status);
        Assert.Equal(1, authority.Reads); Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Discards); Assert.Equal(0, writer.Writes);
        Assert.False(db.ChangeTracker.HasChanges()); Assert.Null(db.Database.CurrentTransaction);
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Synthetic_original_key_replay_rechecks_after_command_read_without_a_second_native_effect(string fault)
    {
        var company = new CostCompany(); var user = new CostUser(); var authority = new CostAuthority(); var store = new SyntheticCostApprovalProtocolStore();
        var service = new ManufacturingCostDifferenceServiceV1(store, store, authority, company, user, new Correlation());
        var first = await service.ApproveAsync(EventId, 2, "original"); Assert.Equal(AccountingApiStatus.Ok, first.Status);
        using var cancellation = new CancellationTokenSource();
        store.AfterFind = () =>
        {
            if (fault == "permission") authority.Granted = false; if (fault == "company") company.Set(Guid.NewGuid());
            if (fault == "actor") user.Actor = Maker; if (fault == "member") user.Member = false; if (fault == "cancel") cancellation.Cancel();
        };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApproveAsync(EventId, 2, "original", cancellation.Token));
        else { var answer = await service.ApproveAsync(EventId, 2, "original"); Assert.Equal(AccountingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource); }
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }

    [Fact]
    public async Task Synthetic_readonly_no_winner_never_mutates_or_records_a_conflict_receipt()
    {
        var store = new SyntheticCostApprovalProtocolStore { ReplayOnly = true };
        var service = new ManufacturingCostDifferenceServiceV1(store, store, new CostAuthority(), new CostCompany(), new CostUser(), new Correlation());
        var answer = await service.ApproveAsync(EventId, 2, "no-winner");
        Assert.Equal(AccountingApiStatus.Conflict, answer.Status); Assert.Equal(AccountingStoreCodes.ConcurrentChange, answer.Code);
        Assert.Equal(0, store.Effects); Assert.Empty(store.Commands); Assert.Equal(1, store.Finds);
    }

    [Fact]
    public async Task Synthetic_success_retains_native_hash_public_wire_and_actor_payload_replay_contract()
    {
        var store = new SyntheticCostApprovalProtocolStore(); var user = new CostUser();
        var service = new ManufacturingCostDifferenceServiceV1(store, store, new CostAuthority(), new CostCompany(), user, new Correlation());
        var original = await service.ApproveAsync(EventId, 2, "native-hash"); var replay = await service.ApproveAsync(EventId, 2, "native-hash");
        Assert.True(replay.IsReplay); Assert.Equal("synthetic-cost", replay.OriginalCorrelationId); Assert.Equal(1, store.Effects);
        var command = Assert.Single(store.Commands); var payload = JsonSerializer.Serialize(new { id = EventId, version = 2L }, Json);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            ManufacturingCostDifferenceCommandNamesV1.Approve + "|" + payload))), command.RequestHash);
        Assert.Equal(8, JsonSerializer.SerializeToElement(original, Json).EnumerateObject().Count());
        Assert.Equal(20, JsonSerializer.SerializeToElement(original.Resource, Json).EnumerateObject().Count());
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await service.ApproveAsync(EventId, 3, "native-hash")).Code);
        user.Actor = Maker; Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await service.ApproveAsync(EventId, 2, "native-hash")).Code);
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }

    [Fact]
    public async Task Synthetic_native_business_refusal_before_save_keeps_its_status_code_and_leaves_the_key_available()
    {
        var store = new SyntheticCostApprovalProtocolStore { FailureBeforeWrite = "cost_difference_account_plan_missing" };
        var service = new ManufacturingCostDifferenceServiceV1(store, store, new CostAuthority(), new CostCompany(), new CostUser(), new Correlation());
        var answer = await service.ApproveAsync(EventId, 2, "business-before-save");
        Assert.Equal(AccountingApiStatus.Unprocessable, answer.Status); Assert.Equal("cost_difference_account_plan_missing", answer.Code);
        Assert.Equal(0, store.Effects); Assert.Empty(store.Commands);
        store.FailureBeforeWrite = null;
        Assert.Equal(AccountingApiStatus.Ok, (await service.ApproveAsync(EventId, 2, "business-before-save")).Status);
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }

    private static AccountingDbContext CostDb(ICompanyContext company) => new(new DbContextOptionsBuilder<AccountingDbContext>()
        .UseInMemoryDatabase("cost-entry-" + Guid.NewGuid()).Options, company);
    private static AccountingCommandRow CostCaller() => new() { CompanyId = Company, CommandName = "SENTETIK caller", IdempotencyKeyHash = "caller", ActorId = "caller", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
    private static AuditEventDraft CostCallerAudit() => new(Company, "SENTETIK caller", "Accounting.ManufacturingCostDifference", "caller", "Approve", 2, 3,
        "User", Checker.ToString("D"), AccountingJournalReversalPermissionNamesV1.Approve, [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None,
        null, "UnitTest", null, null, "SENTETIK-caller", null, "SENTETIK-key");
    private sealed class CostCompany : ICompanyContext { public Guid CompanyId { get; private set; } = Company; public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid id) => CompanyId = id; }
    private sealed class CostUser : ICurrentUser { public Guid Actor = Checker; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class CostAuthority : IErpAuthorityRevalidationService
    {
        public int Reads; public bool Granted = true, CompanyWide = true, Null; public Action? AfterRead;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid id, CancellationToken token = default)
        { Reads++; AfterRead?.Invoke(); return Task.FromResult(Null ? null! : new PermissionScope(Granted, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class CostCollector : IAuditEventCollector { public List<AuditEventDraft> Entries { get; } = []; public void Enqueue(AuditEventDraft draft) => Entries.Add(draft); }
    private sealed class CostEntryWriter(CostCollector collector) : IAuditTransactionWriterV1
    {
        public int Writes, Discards; public bool HasPendingDrafts => collector.Entries.Count != 0;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default) { Writes++; throw new InvalidOperationException("Entry-only fixture cannot write Audit"); }
        public void DiscardPending() { Discards++; collector.Entries.Clear(); }
    }
    // Explicit synthetic lifecycle protocol only. It proves no relational transaction, rollback or durable Audit.
    private sealed class SyntheticCostApprovalProtocolStore : IManufacturingCostDifferenceStoreV1, IAccountingCommandLog
    {
        public bool ReplayOnly; public string? FailureBeforeWrite; public Action? AfterFind; public int Effects, Finds; public List<AccountingCommandRecord> Commands { get; } = [];
        public async Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> RunApprovalAsync(Func<bool, Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>>> attempt,
            Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            try { await fence(token); var answer = await attempt(ReplayOnly); await fence(token); return answer; }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); }
        }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default)
        { Finds++; var answer = Commands.SingleOrDefault(c => c.CommandName == command && c.IdempotencyKeyHash == key); AfterFind?.Invoke(); return Task.FromResult(answer); }
        public Task RecordCommandAsync(AccountingCommandRecord command, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public async Task<ManufacturingCostDifferenceViewV1> ApproveAsync(Guid id, long version, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        { await fence(token); if (FailureBeforeWrite is { } failure) throw new AccountingStoreException(failure, "SENTETIK native refusal before save"); Effects++; var value = View() with { Status = "Posted", Version = version + 1, ApprovedBy = actor, JournalId = Guid.NewGuid() }; Commands.Add(metadata.Completion!(value)); return value; }
        public Task AcceptAsync(GoymenERP.BuildingBlocks.Messaging.WorkInboxEventV1 envelope, ManufacturingCostDifferenceEventV1 source, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ManufacturingCostDifferenceViewV1>> ListAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<ManufacturingCostDifferenceViewV1?> FindAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ManufacturingCostDifferenceViewV1> PrepareAsync(PrepareManufacturingCostDifferenceV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
    }
}
