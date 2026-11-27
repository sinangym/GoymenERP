using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cancellation_owned_native_success_restart_original_key_keeps_outer_version_and_approves_each_real_component_once()
    {
        foreach (var interceptor in new[]{false,true})
        {
            await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);
            AccountingApiResult<AccountingInventoryCostProposalV1> first;
            await using(var owner=new CancellationSafetyOwner(run,setup,interceptor))
            {
                first=await owner.Approve("original");Assert.Equal(AccountingApiStatus.Ok,first.Status);
                Assert.Equal(setup.Request.ExpectedVersion,first.Resource?.Version);Assert.Equal("CancelledByComponentReversal",first.Resource?.Status);
                Assert.Equal(setup.Request.ExpectedCancellationVersion+1,first.Resource?.ComponentCancellation?.Version);
                Assert.Equal("Approved",first.Resource?.ComponentCancellation?.Status);Assert.Equal(Checker,first.Resource?.ComponentCancellation?.ApprovedBy);
                var replay=await owner.Approve("original");Assert.True(replay.IsReplay);Assert.Equal(owner.CorrelationId,replay.OriginalCorrelationId);
                Assert.Equal(JsonSerializer.Serialize(first.Resource,Json),JsonSerializer.Serialize(replay.Resource,Json));
                var command=await owner.Db.Commands.AsNoTracking().SingleAsync(x=>x.CommandName=="accounting.inventory-cost.approve-component-cancellation.v1");
                var payload=JsonSerializer.Serialize(new{id=setup.Id,request=setup.Request},Json);
                Assert.Equal(AccountingInventoryCostSourceValidationV1.Sha256("accounting.inventory-cost.approve-component-cancellation.v1|"+payload),command.PayloadHash);
                Assert.Equal(Checker.ToString("D"),command.ActorId);Assert.Equal(owner.CorrelationId,command.CorrelationId);
                Assert.Equal(JsonSerializer.Serialize(first,Json),command.ResultJson);owner.AssertClean();
            }
            var completed=await CancellationSafetyCounts(run);Assert.Equal(before with{Versions=before.Versions+1,Claims=before.Claims+1,Journals=before.Journals+1,Commands=before.Commands+1,Audits=before.Audits+1},completed);
            await using var restarted=new CancellationSafetyOwner(run,setup,interceptor);
            Assert.True((await restarted.Approve("original")).IsReplay);Assert.Equal(completed,await CancellationSafetyCounts(run));
            var changed=await restarted.Service.ApproveCancellationAsync(setup.Id,setup.Request with{ExpectedCancellationVersion=setup.Request.ExpectedCancellationVersion+1},"original");
            Assert.Equal(AccountingApiCodes.IdempotencyConflict,changed.Code);
            restarted.User.Actor=Maker;Assert.Equal(AccountingApiStatus.Denied,(await restarted.Approve("new-maker")).Status);
            Assert.Equal(completed,await CancellationSafetyCounts(run));restarted.AssertClean();
        }
    }
    [SqlServerFact]
    public async Task Cancellation_owned_entry_refuses_borrowed_ambient_dirty_and_pending_work_without_touching_its_caller()
    {
        foreach(var fault in new[]{"borrowed","ambient","dirty","pending","await-dirty","await-pending"})
        {
            await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);
            await using var owner=new CancellationSafetyOwner(run,setup);var caller=InventoryApprovalCaller();
            if(fault=="dirty")owner.Db.Commands.Add(caller);if(fault=="pending")owner.Collector.Enqueue(InventoryApprovalAudit());
            await using var borrowed=fault=="borrowed"?await owner.Db.Database.BeginTransactionAsync():null;
            using(var ambient=fault=="ambient"?new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled):null)
            {
                owner.Authority.BeforeRead=()=>{if(fault=="await-dirty")owner.Db.Commands.Add(caller);if(fault=="await-pending")owner.Collector.Enqueue(InventoryApprovalAudit());return Task.CompletedTask;};
                Assert.Equal(AccountingApiStatus.Unavailable,(await owner.Approve("entry")).Status);
                Assert.Equal(0,owner.Writer.Writes);Assert.Equal(0,owner.Writer.Discards);
                Assert.Equal(fault is "dirty" or "await-dirty",owner.Db.ChangeTracker.HasChanges());Assert.Equal(fault is "pending" or "await-pending",owner.Writer.HasPendingDrafts);
                if(owner.Db.ChangeTracker.HasChanges())Assert.Same(caller,Assert.Single(owner.Db.ChangeTracker.Entries()).Entity);
                if(borrowed is not null)Assert.Same(borrowed,owner.Db.Database.CurrentTransaction);
            }
            if(borrowed is not null)await borrowed.RollbackAsync();Assert.Equal(before,await CancellationSafetyCounts(run));
        }
    }
    [SqlServerFact]
    public async Task Cancellation_owned_nested_claim_and_component_reads_stop_after_completed_revocation_or_cancellation()
    {
        foreach(var table in new[]{"InvoiceCogsEconomicClaims","InventoryCostCancellationClaims","JournalLines"})
        foreach(var cancel in new[]{false,true})
        {
            await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);
            using var cancellation=new CancellationTokenSource();CancellationSafetyOwner? owner=null;var fired=false;var later=0;
            var probe=new InventoryApprovalQuery(sql=>{if(fired){later++;return;}if(!sql.Contains("["+table+"]",StringComparison.Ordinal))return;
                fired=true;if(cancel)cancellation.Cancel();else owner!.Authority.Granted=false;});
            await using(owner=new CancellationSafetyOwner(run,setup,true,probe))
            {
                if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>owner.Approve("read-fence",cancellation.Token));
                else Assert.Equal(AccountingApiStatus.Denied,(await owner.Approve("read-fence")).Status);
                Assert.True(fired);Assert.Equal(0,later);Assert.Equal(before,await CancellationSafetyCounts(run));owner.AssertClean();
                owner.Authority.Granted=true;Assert.Equal(AccountingApiStatus.Ok,(await owner.Approve("read-fence")).Status);owner.AssertClean();
            }
        }
    }
    [SqlServerFact]
    public async Task Cancellation_owned_after_save_precommit_and_postcommit_authority_fences_rollback_or_suppress_without_duplicate_reversal()
    {
        foreach(var phase in new[]{"after-save","precommit","postcommit","actor","company","member","cancel"})
        {
            await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);
            var save=new CancellationSafetySave();var tx=new InventoryApprovalTransactions();using var cancellation=new CancellationTokenSource();
            await using(var owner=new CancellationSafetyOwner(run,setup,true,save,tx))
            {
                var count=0;var fired=false;owner.Authority.Read=()=>
                {
                    if(save.Completed)count++;
                    // Final cancellation save: postSave1, Atomic2, service3, command4/5, retained reads6..22, receipt23, runner24, precommit25, release26.
                    var trigger=phase=="precommit"?count==25:phase=="postcommit"?tx.Commits==1:save.Completed;
                    if(!trigger||fired)return true;fired=true;
                    if(phase=="actor")owner.User.Actor=Maker;else if(phase=="company")owner.Company.Set(Guid.NewGuid());
                    else if(phase=="member")owner.User.Member=false;else if(phase=="cancel")cancellation.Cancel();else return false;
                    return true;
                };
                if(phase=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>owner.Approve("late-fence",cancellation.Token));
                else Assert.Equal(AccountingApiStatus.Denied,(await owner.Approve("late-fence")).Status);
                Assert.True(fired);owner.AssertClean();Assert.Equal(phase=="postcommit"?1:0,tx.Commits);
                Assert.Equal(phase=="postcommit"?before with{Versions=before.Versions+1,Claims=before.Claims+1,Journals=before.Journals+1,Commands=before.Commands+1,Audits=before.Audits+1}:before,await CancellationSafetyCounts(run));
            }
            await using var retry=new CancellationSafetyOwner(run,setup);var result=await retry.Approve("late-fence");
            Assert.Equal(AccountingApiStatus.Ok,result.Status);Assert.Equal(phase=="postcommit",result.IsReplay);retry.AssertClean();
            Assert.Equal(before with{Versions=before.Versions+1,Claims=before.Claims+1,Journals=before.Journals+1,Commands=before.Commands+1,Audits=before.Audits+1},await CancellationSafetyCounts(run));
        }
    }
    [SqlServerFact]
    public async Task Cancellation_owned_saved_changes_domain_refusal_after_acceptance_aborts_saved_success_and_cleanup_failure_stays_visible()
    {
        foreach(var cleanup in new[]{"none","rollback","dispose"})
        {
            await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);
            var save=new CancellationSafetySave{DomainAfterSave=true};var fault=cleanup=="none"?null:new InventoryApprovalCleanup(cleanup=="dispose");
            var interceptors=fault is null?new IInterceptor[]{save}:new IInterceptor[]{save,fault};
            await using(var owner=new CancellationSafetyOwner(run,setup,true,interceptors))
            {
                if(fault is null){var refused=await owner.Approve("saved-refusal");Assert.Equal(AccountingApiStatus.Unprocessable,refused.Status);Assert.Equal("inventory_cost_posting_refused",refused.Code);}
                else await Assert.ThrowsAsync<AggregateException>(()=>owner.Approve("saved-refusal"));
                Assert.True(save.Injected);Assert.True(save.CleanAtThrow);Assert.Equal(1,save.Attempts);Assert.Equal(1,owner.Writer.Discards);owner.AssertClean();
            }
            Assert.Equal(before,await CancellationSafetyCounts(run));await using var retry=new CancellationSafetyOwner(run,setup);
            Assert.Equal(AccountingApiStatus.Ok,(await retry.Approve("saved-refusal")).Status);Assert.True((await retry.Approve("saved-refusal")).IsReplay);
            Assert.Equal(before with{Versions=before.Versions+1,Claims=before.Claims+1,Journals=before.Journals+1,Commands=before.Commands+1,Audits=before.Audits+1},await CancellationSafetyCounts(run));
        }
    }
    [SqlServerFact]
    public async Task Cancellation_owned_controlled_1205_and_concurrency_start_only_one_readonly_original_key_pass()
    {
        foreach(var deadlock in new[]{false,true})foreach(var winner in new[]{false,true})
        {
            await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);
            var save=new CancellationSafetySave{Concurrency=!deadlock,Deadlock=deadlock};var tx=new InventoryApprovalTransactions();var won=false;
            await using var owner=new CancellationSafetyOwner(run,setup,true,save,tx);
            owner.Authority.BeforeRead=async()=>
            {
                if(tx.Starts!=2||won||!winner)return;won=true;
                await using var other=new CancellationSafetyOwner(run,setup);
                Assert.Equal(AccountingApiStatus.Ok,(await other.Approve("race")).Status);
            };
            var answer=await owner.Approve("race");Assert.True(save.Injected);Assert.Equal(2,tx.Starts);Assert.Equal(1,save.Attempts);
            Assert.Equal(winner?AccountingApiStatus.Ok:AccountingApiStatus.Conflict,answer.Status);Assert.Equal(winner,answer.IsReplay);
            Assert.Equal(winner,won);owner.AssertClean();
            Assert.Equal(winner?before with{Versions=before.Versions+1,Claims=before.Claims+1,Journals=before.Journals+1,Commands=before.Commands+1,Audits=before.Audits+1}:before,await CancellationSafetyCounts(run));
        }
        // Provider faults above are injected in real SQL saves; they do not prove a simultaneous server deadlock interleaving.
    }
    [SqlServerFact]
    public async Task Cancellation_readonly_evidence_callback_preserves_borrowed_transaction_caller_rows_and_actual_audit_on_nested_denial()
    {
        foreach(var cancel in new[]{false,true})
        {
            await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);
            CancellationSafetyOwner? owner=null;var armed=false;var fired=false;var successors=0;using var cancellation=new CancellationTokenSource();
            var probe=new InventoryApprovalQuery(sql=>
            {
                if(!armed)return;
                if(sql.Contains("[InvoiceCogsEconomicClaimSuccessions]",StringComparison.Ordinal))successors++;
                if(fired||!sql.Contains("[InvoiceCogsEconomicClaims]",StringComparison.Ordinal))return;
                fired=true;if(cancel)cancellation.Cancel();else owner!.Authority.Granted=false;
            });
            await using(owner=new CancellationSafetyOwner(run,setup,true,probe))
            {
                await using var transaction=await owner.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var caller=InventoryApprovalCaller();owner.Db.Commands.Add(caller);owner.Collector.Enqueue(InventoryApprovalAudit());
                var profiles=new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(Company);
                var ledger=new ManufacturingPostedCostAttributionStoreV1(owner.Db,owner.Company,setup.Physical,new(profiles),TimeProvider.System);
                var reader=new InvoiceLineCogsCorrectionStoreV1(owner.Db,owner.Company,new InventoryApprovalPlan(),owner.Collector,owner.Writer,TimeProvider.System,ledger,
                    new PurchasePostedCostAttributionStoreV1(owner.Db,owner.Company));
                // Native immutable view is read before arming the inner-claim revocation probe.
                var row=await owner.Db.InventoryCostProposals.AsNoTracking().SingleAsync(x=>x.Id==setup.Id&&x.Version==setup.Request.ExpectedVersion);
                var view=JsonSerializer.Deserialize<AccountingInventoryCostProposalV1>(row.ViewJson,Json)!;
                async Task ReadFence(CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();var permission=await owner.Authority.RevalidatePermissionScopeAsync(AccountingJournalReversalPermissionNamesV1.Approve,Company,token);
                    token.ThrowIfCancellationRequested();if(!permission.IsGranted||!permission.IsCompanyWide)throw new NormalJournalAuthorityDeniedException();
                }
                armed=true;
                if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>reader.ReadCancellationComponentsAsync(view,cancellation.Token,ReadFence));
                else await Assert.ThrowsAsync<NormalJournalAuthorityDeniedException>(()=>reader.ReadCancellationComponentsAsync(view,default,ReadFence));
                Assert.True(fired);Assert.Equal(0,successors);Assert.Same(transaction,owner.Db.Database.CurrentTransaction);
                Assert.Same(caller,Assert.Single(owner.Db.ChangeTracker.Entries<AccountingCommandRow>()).Entity);
                Assert.True(owner.Writer.HasPendingDrafts);Assert.Equal(0,owner.Writer.Writes);Assert.Equal(0,owner.Writer.Discards);
                // The same local reader must release its ephemeral callback after failure, leaving raw read semantics intact.
                armed=false;var raw=await reader.ReadCancellationComponentsAsync(view,default);Assert.NotEmpty(raw.Components);
                Assert.True(owner.Writer.HasPendingDrafts);Assert.Equal(0,owner.Writer.Discards);await transaction.RollbackAsync();
            }
            Assert.Equal(before,await CancellationSafetyCounts(run));
        }
    }
    [SqlServerFact]
    public async Task Cancellation_owned_terminal_version_refusal_keeps_original_key_answer_after_a_separate_valid_approval()
    {
        await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);
        await using var owner=new CancellationSafetyOwner(run,setup);
        var invalid=setup.Request with{ExpectedCancellationVersion=99};
        var first=await owner.Service.ApproveCancellationAsync(setup.Id,invalid,"old-refusal");
        Assert.Equal(AccountingApiStatus.Conflict,first.Status);Assert.Equal(AccountingApiCodes.VersionConflict,first.Code);
        Assert.Equal(99L,first.ExpectedVersion);Assert.Equal(setup.Request.ExpectedCancellationVersion,first.CurrentVersion);Assert.Null(first.Resource);
        Assert.Equal(before with{Commands=before.Commands+1},await CancellationSafetyCounts(run));owner.AssertClean();
        Assert.Equal(AccountingApiStatus.Ok,(await owner.Approve("new-valid")).Status);
        var replay=await owner.Service.ApproveCancellationAsync(setup.Id,invalid,"old-refusal");
        Assert.True(replay.IsReplay);Assert.Equal(owner.CorrelationId,replay.OriginalCorrelationId);Assert.Equal(first.Status,replay.Status);
        Assert.Equal(first.Code,replay.Code);Assert.Equal(first.ExpectedVersion,replay.ExpectedVersion);Assert.Equal(first.CurrentVersion,replay.CurrentVersion);Assert.Null(replay.Resource);
        Assert.Equal(before with{Versions=before.Versions+1,Claims=before.Claims+1,Journals=before.Journals+1,Commands=before.Commands+2,Audits=before.Audits+1},await CancellationSafetyCounts(run));owner.AssertClean();
    }
    private sealed record CancellationSafetySetup(Guid Id,ApproveInventoryCostCancellationV1 Request,IAccountingInventoryCostSourceReaderV1 Reader,AttributionLineage Physical);
    private static async Task<CancellationSafetySetup> CancellationSafetyPrepare(Run run)
    {
        var period=await run.Period();var physical=new AttributionLineage(run.Source.Facts);await using var scope=run.Open();var ledger=AttributionLedger(scope,physical);
        await PostAttributed(scope,physical,ledger,period);var actual=new AttributedCostReader(run.Source.Facts,ledger,physical);var reader=new CancellationCostReader(actual,run.Source.Facts);
        var store=AttributedCosts(scope,reader,ledger);await store.AcceptAsync(actual.Envelope);await CancelCostInvoice(scope,run.Source.Facts);
        var item=Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        var prepared=await store.ProposeReversalAsync(item.ProposalId,CancelRequest(item,period),Maker,Fence,Metadata(Maker));
        Assert.Equal("Prepared",prepared.ComponentCancellation?.Status);
        return new(item.ProposalId,new(item.Version,prepared.ComponentCancellation!.Version),reader,physical);
    }
    private sealed record CancellationSafetyCount(int Versions,int Claims,int Journals,int Commands,int Audits,int Deliveries);
    private static async Task<CancellationSafetyCount> CancellationSafetyCounts(Run run)=>new(await run.Count("accounting.InventoryCostCancellationVersions"),await run.Count("accounting.InventoryCostCancellationClaims"),await run.Count("accounting.Journals"),await run.Count("accounting.Commands"),await run.Count("audit.AuditEvents"),await run.Count("accounting.Deliveries"));
    private sealed class CancellationSafetyOwner:IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public InventoryApprovalCompany Company{get;}=new();public InventoryApprovalUser User{get;}=new();public InventoryApprovalAuthority Authority{get;}=new();
        public AccountingDbContext Db{get;}public AuditEventCollector Collector{get;}public InventoryApprovalWriter Writer{get;}
        public AccountingInventoryCostServiceV1 Service{get;}public string CorrelationId{get;}="SENTETIK-cancellation-"+Guid.NewGuid().ToString("N");
        private readonly CancellationSafetySetup setup;
        public CancellationSafetyOwner(Run run,CancellationSafetySetup setup,bool auditInterceptor=true,params IInterceptor[] probes)
        {
            this.setup=setup;auditScope=run.Factory.Services.CreateScope();auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests.Company);
            Collector=auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();Writer=new(auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var options=new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString);
            if(auditInterceptor)options.AddInterceptors(new AuditSaveChangesInterceptor(Collector,Company));options.AddInterceptors(probes);
            foreach(var fault in probes.OfType<InventoryApprovalCleanup>())fault.Configure(options);
            Db=new(options.Options,Company);var profiles=new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests.Company);
            var core=new AccountingStore(Db,Company,Collector,TimeProvider.System,new(profiles),normalApprovalAuditWriter:Writer);
            var ledger=new ManufacturingPostedCostAttributionStoreV1(Db,Company,setup.Physical,new(profiles),TimeProvider.System);
            var store=new AccountingInventoryCostStoreV1(Db,core,setup.Reader,new CogsMapping(),new InventoryApprovalPlan(),new InventoryApprovalCurrencies(),new(profiles),Company,Collector,Writer,TimeProvider.System,ledger,
                postedPurchasing:new PurchasePostedCostAttributionStoreV1(Db,Company));
            var correlation=new CorrelationContext();correlation.Set(CorrelationId,null,"Web",null,null);Service=new(store,core,Authority,Company,User,correlation);
        }
        public Task<AccountingApiResult<AccountingInventoryCostProposalV1>> Approve(string key,CancellationToken token=default)=>Service.ApproveCancellationAsync(setup.Id,setup.Request,key,token);
        public void AssertClean(){Assert.False(Db.ChangeTracker.HasChanges());Assert.False(Writer.HasPendingDrafts);Assert.Null(Db.Database.CurrentTransaction);}
        public async ValueTask DisposeAsync(){try{await Db.DisposeAsync();}finally{auditScope.Dispose();}}
    }
    private sealed class CancellationSafetySave:SaveChangesInterceptor
    {
        public bool DomainAfterSave,Concurrency,Deadlock,Injected,Completed,CleanAtThrow;public int Attempts;private bool finalSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {
            finalSave=data.Context!.ChangeTracker.Entries<InventoryCostCancellationVersionRow>().Any(e=>e.State==EntityState.Added&&e.Entity.Status==2);
            if(finalSave){Attempts++;if(!Injected&&(Concurrency||Deadlock)){Injected=true;if(Concurrency)throw new DbUpdateConcurrencyException("SENTETIK cancellation save race");throw new DbUpdateException("SENTETIK cancellation SQL1205",SqlConcurrencyWrapperPolicyTests.SqlFailure(1205));}}
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken token=default)
        {
            if(finalSave){Completed=true;if(DomainAfterSave&&!Injected){Injected=true;CleanAtThrow=!data.Context!.ChangeTracker.HasChanges();throw new AccountingDomainException("SENTETIK-after-save", "SENTETIK after EF accepted native success");}}
            return ValueTask.FromResult(result);
        }
    }
}
