using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingInventoryCostServiceFenceTests
{
    private static readonly Guid SafetyId = Guid.Parse("a5193000-0000-4000-8000-000000000055");
    [Theory]
    [InlineData("missing-writer")] [InlineData("other-db")] [InlineData("other-collector")] [InlineData("other-writer")]
    [InlineData("nonrelational")] [InlineData("dirty")] [InlineData("pending")] [InlineData("ambient")]
    [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Production_inventory_cost_approval_refuses_entry_without_adopting_or_cleaning_caller_work(string fault)
    {
        var company = new MutableCompany(); var collector = new SafetyCollector(); var writer = new SafetyWriter(collector);
        await using var db = SafetyDb(company); await using var other = SafetyDb(company);
        var core = new AccountingStore(fault == "other-db" ? other : db, company,
            fault == "other-collector" ? new SafetyCollector() : collector, TimeProvider.System, null!,
            normalApprovalAuditWriter: fault == "missing-writer" ? null : fault == "other-writer" ? new SafetyWriter(collector) : writer);
        var store = SafetyStore(db, core, company, collector, writer); var row = SafetyCaller();
        if (fault == "dirty") db.Commands.Add(row); if (fault == "pending") collector.Enqueue(SafetyAudit());
        using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        var attempts = 0;
        var answer = await store.RunApprovalAsync(_ => { attempts++; throw new InvalidOperationException("No native owner IO allowed"); }, _ =>
        { if (fault == "await-dirty") db.Commands.Add(row); if (fault == "await-pending") collector.Enqueue(SafetyAudit()); return Task.CompletedTask; });
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Equal(0, attempts);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(fault is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
        Assert.Equal(fault is "pending" or "await-pending", writer.HasPendingDrafts);
        if (db.ChangeTracker.HasChanges()) Assert.Same(row, Assert.Single(db.ChangeTracker.Entries()).Entity);
    }
    [Fact]
    public async Task An_unconfigured_legacy_store_has_no_fake_approval_transaction_success()
    {
        IAccountingInventoryCostStoreV1 store = new FailingOptionsStore(); var effects = 0;
        var answer = await store.RunApprovalAsync(_ => { effects++; throw new InvalidOperationException(); }, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Equal(0, effects);
    }
    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")]
    [InlineData("scope")] [InlineData("null-scope")] [InlineData("cancel")]
    public async Task Initial_await_denial_or_completed_cancellation_keeps_pending_caller_audit_untouched(string fault)
    {
        var company = new MutableCompany(); var user = new MutableUser(); var authority = new SafetyAuthority();
        await using var db = SafetyDb(company); var collector = new SafetyCollector(); collector.Enqueue(SafetyAudit()); var writer = new SafetyWriter(collector);
        var core = new AccountingStore(db, company, collector, TimeProvider.System, null!, normalApprovalAuditWriter: writer);
        var store = SafetyStore(db, core, company, collector, writer); using var cancellation = new CancellationTokenSource();
        authority.AfterRead = () => Change(fault, company, user, authority, cancellation);
        var service = new AccountingInventoryCostServiceV1(store, new UnusedCommands(), authority, company, user, SafetyCorrelation());
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApproveAsync(SafetyId, AccountingInventoryCostKindV1.SalesInvoicedShipment, 7, "capture", cancellation.Token));
        else Assert.Equal(AccountingApiStatus.Denied, (await service.ApproveAsync(SafetyId, AccountingInventoryCostKindV1.SalesInvoicedShipment, 7, "capture")).Status);
        Assert.Equal(1, authority.Reads); Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Discards); Assert.Equal(0, writer.Writes);
        Assert.False(db.ChangeTracker.HasChanges()); Assert.Null(db.Database.CurrentTransaction);
    }
    [Theory]
    [InlineData(AccountingInventoryCostKindV1.SalesInvoicedShipment, "Posted")]
    [InlineData(AccountingInventoryCostKindV1.PurchaseInvoiceDifference, "RoundedNoCost")]
    [InlineData(AccountingInventoryCostKindV1.PurchaseInvoiceDifference, "Stale")]
    public async Task Synthetic_protocol_preserves_N_to_Nplus1_native_status_hash_wire_and_original_actor_receipt(AccountingInventoryCostKindV1 kind, string status)
    {
        var store = new SafetyProtocolStore { Status = status }; var user = new MutableUser(); var authority = new SafetyAuthority();
        var service = new AccountingInventoryCostServiceV1(store, store, authority, new MutableCompany(), user, SafetyCorrelation());
        var first = await service.ApproveAsync(SafetyId, kind, 7, "native-key"); var replay = await service.ApproveAsync(SafetyId, kind, 7, "native-key");
        Assert.Equal(status == "Stale" ? AccountingApiStatus.Conflict : AccountingApiStatus.Ok, first.Status);
        Assert.Equal(status, first.Resource?.Status); Assert.Equal(8L, first.Resource?.Version);
        Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-inventory-approval", replay.OriginalCorrelationId);
        Assert.Equal(1, store.Effects); var command = Assert.Single(store.Commands);
        var payload = JsonSerializer.Serialize(new { id = SafetyId, kind, version = 7L });
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("accounting.inventory-cost.approve.v1|" + payload))), command.RequestHash);
        Assert.Equal(kind == AccountingInventoryCostKindV1.SalesInvoicedShipment ? NormalJournalPermissionNamesV1.DraftApprove : AccountingJournalReversalPermissionNamesV1.Approve, authority.Permissions[0]);
        Assert.Equal(8, JsonSerializer.SerializeToElement(first, new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateObject().Count());
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await service.ApproveAsync(SafetyId, kind, 8, "native-key")).Code);
        user.UserId = Guid.NewGuid().ToString("D"); Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await service.ApproveAsync(SafetyId, kind, 7, "native-key")).Code);
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }
    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")] [InlineData("cancel")]
    public async Task Synthetic_original_key_read_revalidates_before_releasing_its_saved_answer(string fault)
    {
        var company = new MutableCompany(); var user = new MutableUser(); var authority = new SafetyAuthority(); var store = new SafetyProtocolStore();
        var service = new AccountingInventoryCostServiceV1(store, store, authority, company, user, SafetyCorrelation());
        Assert.Equal(AccountingApiStatus.Ok, (await service.ApproveAsync(SafetyId, AccountingInventoryCostKindV1.SalesInvoicedShipment, 7, "original")).Status);
        using var cancellation = new CancellationTokenSource(); store.AfterFind = () => Change(fault, company, user, authority, cancellation);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApproveAsync(SafetyId, AccountingInventoryCostKindV1.SalesInvoicedShipment, 7, "original", cancellation.Token));
        else { var answer = await service.ApproveAsync(SafetyId, AccountingInventoryCostKindV1.SalesInvoicedShipment, 7, "original"); Assert.Equal(AccountingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource); }
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }
    [Fact]
    public async Task Synthetic_readonly_no_winner_never_mutates_or_records_a_new_terminal_attempt()
    {
        var store = new SafetyProtocolStore { ReplayOnly = true };
        var service = new AccountingInventoryCostServiceV1(store, store, new SafetyAuthority(), new MutableCompany(), new MutableUser(), SafetyCorrelation());
        var answer = await service.ApproveAsync(SafetyId, AccountingInventoryCostKindV1.SalesInvoicedShipment, 7, "no-winner");
        Assert.Equal(AccountingApiStatus.Conflict, answer.Status); Assert.Equal(AccountingStoreCodes.ConcurrentChange, answer.Code);
        Assert.Equal(0, store.Effects); Assert.Empty(store.Commands); Assert.Equal(1, store.Finds);
    }
    private static void Change(string fault, MutableCompany company, MutableUser user, SafetyAuthority authority, CancellationTokenSource cancellation)
    {
        if (fault == "company") company.Set(Guid.NewGuid()); if (fault == "actor") user.UserId = Guid.NewGuid().ToString("D");
        if (fault == "member") user.HasMembership = false; if (fault == "permission") authority.Granted = false;
        if (fault == "scope") authority.CompanyWide = false; if (fault == "null-scope") authority.Null = true; if (fault == "cancel") cancellation.Cancel();
    }
    private static CorrelationContext SafetyCorrelation() { var c = new CorrelationContext(); c.Set("SENTETIK-inventory-approval", null, SourceChannels.Web, null, null); return c; }
    private static AccountingDbContext SafetyDb(ICompanyContext company) => new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("inventory-approval-" + Guid.NewGuid()).Options, company);
    private static AccountingInventoryCostStoreV1 SafetyStore(AccountingDbContext db, AccountingStore core, ICompanyContext company, IAuditEventCollector collector, IAuditTransactionWriterV1 writer) =>
        new(db, core, null!, null!, null!, null!, null!, company, collector, writer, TimeProvider.System);
    private static AccountingCommandRow SafetyCaller() => new() { CompanyId = Company, CommandName = "SENTETIK-caller", IdempotencyKeyHash = "caller", ActorId = "caller", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
    private static AuditEventDraft SafetyAudit() => new(Company, "SENTETIK-caller", "Accounting.InventoryCost", "caller", "Approve", 7, 8, "User", Actor.ToString("D"), NormalJournalPermissionNamesV1.DraftApprove,
        [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None, null, "UnitTest", null, null, "SENTETIK-caller", null, "key");
    private sealed class SafetyCollector : IAuditEventCollector { internal List<AuditEventDraft> Entries { get; } = []; public void Enqueue(AuditEventDraft draft) => Entries.Add(draft); }
    private sealed class SafetyWriter(SafetyCollector collector) : IAuditTransactionWriterV1
    { internal int Writes, Discards; public bool HasPendingDrafts => collector.Entries.Count != 0; public Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default) { Writes++; throw new InvalidOperationException("Entry-only writer"); } public void DiscardPending() { Discards++; collector.Entries.Clear(); } }
    private sealed class SafetyAuthority : IErpAuthorityRevalidationService
    {
        internal int Reads; internal bool Granted = true, CompanyWide = true, Null; internal Action? AfterRead; internal List<string> Permissions { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Reads++; Permissions.Add(permission); AfterRead?.Invoke(); return Task.FromResult(Null ? null! : new PermissionScope(Granted, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    // Explicit lifecycle protocol. This fixture proves neither actual SQL rollback nor durable Audit acceptance.
    private sealed class SafetyProtocolStore : IAccountingInventoryCostStoreV1, IAccountingCommandLog
    {
        internal string Status = "Posted"; internal bool ReplayOnly; internal Action? AfterFind; internal int Effects, Finds; internal List<AccountingCommandRecord> Commands { get; } = [];
        public async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunApprovalAsync(Func<bool, Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { try { await fence(token); var answer = await attempt(ReplayOnly); await fence(token); token.ThrowIfCancellationRequested(); return answer; } catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); } }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default)
        { Finds++; var found = Commands.SingleOrDefault(c => c.CommandName == command && c.IdempotencyKeyHash == key); AfterFind?.Invoke(); return Task.FromResult(found); }
        public Task RecordCommandAsync(AccountingCommandRecord command, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public Task RecordAttemptAsync(AccountingCommandRecord command, Func<CancellationToken, Task> fence, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public async Task<AccountingInventoryCostProposalV1> ApproveAsync(Guid id, AccountingInventoryCostKindV1 kind, long version, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            await fence(token); Effects++;
            var value = new AccountingInventoryCostProposalV1(id, Company, Guid.NewGuid(), kind, Guid.NewGuid(), 1, Guid.NewGuid(), new(2026, 11, 17), "SENTETIK",
                new("SENTETIK component", Guid.NewGuid(), null, null, null, new("1", "1"), "{}", new string('A', 64)), version + 1, Status, DateTimeOffset.UtcNow, ApprovedBy: actor);
            Commands.Add(metadata.Completion!(value)); return value;
        }
        public Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountingInventoryCostProposalV1>> ListAsync(AccountingInventoryCostKindV1 kind, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> PrepareAsync(Guid id, AccountingInventoryCostKindV1 kind, PrepareAccountingInventoryCostV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> ProposeReversalAsync(Guid id, ProposeAccountingInventoryCostReversalV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> ApproveCancellationAsync(Guid id, ApproveInventoryCostCancellationV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
    }
}
