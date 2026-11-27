using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingInventoryCostServiceFenceTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("other-db")] [InlineData("other-collector")] [InlineData("other-writer")]
    [InlineData("nonrelational")] [InlineData("dirty")] [InlineData("pending")] [InlineData("ambient")]
    [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Component_cancellation_owned_entry_preserves_caller_rows_and_pending_audit_without_native_attempt(string fault)
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
        var answer = await store.RunCancellationApprovalAsync(_ => { attempts++; throw new InvalidOperationException("No cancellation IO allowed"); }, _ =>
        { if (fault == "await-dirty") db.Commands.Add(row); if (fault == "await-pending") collector.Enqueue(SafetyAudit()); return Task.CompletedTask; });
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Equal(0, attempts);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(fault is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
        Assert.Equal(fault is "pending" or "await-pending", writer.HasPendingDrafts);
        if (db.ChangeTracker.HasChanges()) Assert.Same(row, Assert.Single(db.ChangeTracker.Entries()).Entity);
    }
    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")]
    [InlineData("scope")] [InlineData("null-scope")] [InlineData("cancel")]
    public async Task Component_cancellation_capture_precedes_first_await_and_never_cleans_caller_audit(string fault)
    {
        var company = new MutableCompany(); var user = new MutableUser(); var authority = new SafetyAuthority();
        await using var db = SafetyDb(company); var collector = new SafetyCollector(); collector.Enqueue(SafetyAudit()); var writer = new SafetyWriter(collector);
        var core = new AccountingStore(db, company, collector, TimeProvider.System, null!, normalApprovalAuditWriter: writer);
        var store = SafetyStore(db, core, company, collector, writer); using var cancellation = new CancellationTokenSource();
        authority.AfterRead = () => Change(fault, company, user, authority, cancellation);
        var service = new AccountingInventoryCostServiceV1(store, new UnusedCommands(), authority, company, user, SafetyCorrelation());
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApproveCancellationAsync(SafetyId, new(3,1), "capture", cancellation.Token));
        else Assert.Equal(AccountingApiStatus.Denied, (await service.ApproveCancellationAsync(SafetyId, new(3,1), "capture")).Status);
        Assert.Equal(1, authority.Reads); Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Discards); Assert.Equal(0, writer.Writes);
        Assert.False(db.ChangeTracker.HasChanges()); Assert.Null(db.Database.CurrentTransaction);
    }
    [Fact]
    public async Task Component_cancellation_unconfigured_port_is_unavailable_and_precancel_never_reads_authority()
    {
        IAccountingInventoryCostStoreV1 unconfigured = new FailingOptionsStore(); var effects = 0;
        Assert.Equal(AccountingApiStatus.Unavailable, (await unconfigured.RunCancellationApprovalAsync(_ => { effects++; throw new InvalidOperationException(); }, _ => Task.CompletedTask)).Status);
        Assert.Equal(0,effects);
        var port = new CancellationProtocolStore(); var authority = new SafetyAuthority(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var service = new AccountingInventoryCostServiceV1(port,port,authority,new MutableCompany(),new MutableUser(),SafetyCorrelation());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApproveCancellationAsync(SafetyId,new(3,1),"cancelled",cancellation.Token));
        Assert.Equal(0,authority.Reads); Assert.Equal(0,port.Finds); Assert.Equal(0,port.Effects);
    }
    [Fact]
    public async Task Component_cancellation_native_payload_keeps_outer_version_and_increments_only_cancellation_original_once()
    {
        var port = new CancellationProtocolStore(); var user = new MutableUser(); var authority = new SafetyAuthority();
        var service = new AccountingInventoryCostServiceV1(port,port,authority,new MutableCompany(),user,SafetyCorrelation()); var request = new ApproveInventoryCostCancellationV1(7,4);
        var first = await service.ApproveCancellationAsync(SafetyId,request,"original"); var replay = await service.ApproveCancellationAsync(SafetyId,request,"original");
        Assert.Equal(AccountingApiStatus.Ok,first.Status); Assert.Equal(7L,first.Resource?.Version); Assert.Equal(5L,first.Resource?.ComponentCancellation?.Version);
        Assert.Equal("CancelledByComponentReversal",first.Resource?.Status); Assert.True(replay.IsReplay); Assert.Equal(1,port.Effects);
        var command=Assert.Single(port.Commands); var nativePayload=JsonSerializer.Serialize(new{id=SafetyId,request},new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("accounting.inventory-cost.approve-component-cancellation.v1|"+nativePayload))),command.RequestHash);
        Assert.All(authority.Permissions,p=>Assert.Equal(AccountingJournalReversalPermissionNamesV1.Approve,p));
        Assert.Equal(8,JsonSerializer.SerializeToElement(first,new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateObject().Count());
        Assert.Equal(AccountingApiCodes.IdempotencyConflict,(await service.ApproveCancellationAsync(SafetyId,new(7,5),"original")).Code);
        user.UserId=Guid.NewGuid().ToString("D"); Assert.Equal(AccountingApiCodes.IdempotencyConflict,(await service.ApproveCancellationAsync(SafetyId,request,"original")).Code);
        Assert.Equal(1,port.Effects); Assert.Single(port.Commands);
    }
    [Theory] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")] [InlineData("cancel")]
    public async Task Component_cancellation_original_key_read_rechecks_authority_after_normally_completed_await(string fault)
    {
        var port=new CancellationProtocolStore(); var company=new MutableCompany(); var user=new MutableUser(); var authority=new SafetyAuthority();
        var service=new AccountingInventoryCostServiceV1(port,port,authority,company,user,SafetyCorrelation()); var request=new ApproveInventoryCostCancellationV1(7,4);
        Assert.Equal(AccountingApiStatus.Ok,(await service.ApproveCancellationAsync(SafetyId,request,"original")).Status);
        using var cancellation=new CancellationTokenSource(); port.AfterFind=()=>Change(fault,company,user,authority,cancellation);
        if(fault=="cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.ApproveCancellationAsync(SafetyId,request,"original",cancellation.Token));
        else { var answer=await service.ApproveCancellationAsync(SafetyId,request,"original"); Assert.Equal(AccountingApiStatus.Denied,answer.Status); Assert.Null(answer.Resource); }
        Assert.Equal(1,port.Effects); Assert.Single(port.Commands);
    }
    [Fact]
    public async Task Component_cancellation_readonly_winner_pass_without_original_never_reenters_mutation()
    {
        var port=new CancellationProtocolStore{ReplayOnly=true}; var service=new AccountingInventoryCostServiceV1(port,port,new SafetyAuthority(),new MutableCompany(),new MutableUser(),SafetyCorrelation());
        var answer=await service.ApproveCancellationAsync(SafetyId,new(7,4),"missing"); Assert.Equal(AccountingApiStatus.Conflict,answer.Status);
        Assert.Equal(AccountingStoreCodes.ConcurrentChange,answer.Code); Assert.Equal(0,port.Effects); Assert.Equal(1,port.Finds); Assert.Empty(port.Commands);
    }
    // Explicit lifecycle protocol. This fixture proves neither actual SQL rollback nor durable Audit acceptance.
    private sealed class CancellationProtocolStore : IAccountingInventoryCostStoreV1, IAccountingCommandLog
    {
        internal bool ReplayOnly; internal Action? AfterFind; internal int Effects, Finds; internal List<AccountingCommandRecord> Commands { get; } = [];
        public async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunCancellationApprovalAsync(Func<bool, Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { try { await fence(token); var answer = await attempt(ReplayOnly); await fence(token); token.ThrowIfCancellationRequested(); return answer; } catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); } }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default)
        { Finds++; var found = Commands.SingleOrDefault(c => c.CommandName == command && c.IdempotencyKeyHash == key); AfterFind?.Invoke(); return Task.FromResult(found); }
        public Task RecordCommandAsync(AccountingCommandRecord command, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public Task RecordAttemptAsync(AccountingCommandRecord command, Func<CancellationToken, Task> fence, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public Task<AccountingInventoryCostProposalV1> ApproveAsync(Guid id, AccountingInventoryCostKindV1 kind, long version, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountingInventoryCostProposalV1>> ListAsync(AccountingInventoryCostKindV1 kind, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> PrepareAsync(Guid id, AccountingInventoryCostKindV1 kind, PrepareAccountingInventoryCostV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> ProposeReversalAsync(Guid id, ProposeAccountingInventoryCostReversalV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public async Task<AccountingInventoryCostProposalV1> ApproveCancellationAsync(Guid id, ApproveInventoryCostCancellationV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            await fence(token); token.ThrowIfCancellationRequested(); Effects++;
            var cancellation = new InventoryCostCancellationV1(Guid.NewGuid(), Company, id, Guid.NewGuid(), request.ExpectedCancellationVersion + 1,
                "Approved", 3, Guid.NewGuid(), new(2026,11,18), new(2026,11,18), "SENTETIK reason", Guid.NewGuid(), DateTimeOffset.UtcNow,
                [], [], actor, DateTimeOffset.UtcNow);
            var value = new AccountingInventoryCostProposalV1(id, Company, Guid.NewGuid(), AccountingInventoryCostKindV1.SalesInvoicedShipment,
                Guid.NewGuid(), 1, Guid.NewGuid(), new(2026,11,17), "SENTETIK", new("component", Guid.NewGuid(), null, null, null,
                new("1","1"), "{}", new('A',64)), request.ExpectedVersion, "CancelledByComponentReversal", DateTimeOffset.UtcNow,
                ComponentCancellation: cancellation);
            Commands.Add(metadata.Completion!(value)); return value;
        }
    }
}
