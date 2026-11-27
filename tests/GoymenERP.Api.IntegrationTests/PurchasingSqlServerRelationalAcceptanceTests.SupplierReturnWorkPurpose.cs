using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Work_supplier_return_rejection_uses_native_immutable_receipt_withheld_cost_and_rejects_legacy_current_fallback()
    {
        await using var run=await Run.StartAsync();var(order,id)=await SeedRejectionReturnAsync(run);var checker=Guid.NewGuid();
        await GrantReturnDisclosureActorAsync(run,checker,price:false);
        await using var scope=run.Factory.Services.CreateAsyncScope();var services=scope.ServiceProvider;
        var host=SupplierReturnPurposeHost(services,checker,new SupplierReturnPurposeDelegations(),out _);
        var preview=await host.PreviewAsync(WorkDecisionHostV1.SupplierReturnCostException,id,default);
        Assert.True(preview.Pending);Assert.True(preview.RejectionSupported);Assert.False(preview.ApprovalReasonRequired);
        Assert.Equal(order,preview.SupplierReturnCostException!.PurchaseOrderId);Assert.Equal(PurchasingPriceDisclosureV1.Withheld,preview.SupplierReturnCostException.PriceDisclosure);
        Assert.Null(preview.SupplierReturnCostException.UnitCostTryPerKg);
        var body=new WorkDecisionRequestV1("1",false,"SENTETIK Work rejection");
        var answer=await host.DecideAsync(WorkDecisionHostV1.SupplierReturnCostException,id,body,"supplier-work-reject",default);
        Assert.Equal(("Rejected",false),(answer.State,answer.Replayed));
        var replay=await host.DecideAsync(WorkDecisionHostV1.SupplierReturnCostException,id,body,"supplier-work-reject",default);
        Assert.True(replay.Replayed);Assert.Equal(answer.OriginalCorrelationId,replay.OriginalCorrelationId);
        Assert.Equal(2,await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE ReturnId=@id",("@id",id)));
        Assert.Equal(1,await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'purchasing.supplier-return.cost-exception-rejected.v1' AND EntityId=@id",("@id",id.ToString("D"))));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id=(SELECT MovementId FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id)",("@id",id)));
        // Genuine older receipt shape: native may read the current return, but Work must not manufacture an immutable result.
        await run.ExecuteAsync("UPDATE purchasing.PurchaseSupplierReturnAttempts SET ResultJson=NULL WHERE ReturnId=@id AND CommandName=@command",("@id",id),("@command",PurchaseSupplierReturnCommandsV1.RejectCostExceptionV1));
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>host.DecideAsync(WorkDecisionHostV1.SupplierReturnCostException,id,body,"supplier-work-reject",default))).Status);
        Assert.Equal(2,await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE ReturnId=@id",("@id",id)));
    }

    [SqlServerFact]
    public async Task Work_supplier_return_post_save_delegation_loss_rolls_back_native_receipt_and_real_audit_then_same_key_succeeds()
    {
        await using var run=await Run.StartAsync();var(_,id)=await SeedRejectionReturnAsync(run);var checker=Guid.NewGuid();await GrantReturnDisclosureActorAsync(run,checker,price:false);
        await using var scope=run.Factory.Services.CreateAsyncScope();var services=scope.ServiceProvider;var delegation=new SupplierReturnPurposeDelegations();
        var host=SupplierReturnPurposeHost(services,checker,delegation,out var authority);
        var db=services.GetRequiredService<PurchasingDbContext>();var observed=false;
        authority.BeforeRead=()=>{if(db.ChangeTracker.Entries<PurchaseSupplierReturnRow>().Any(e=>e.State==EntityState.Unchanged && e.Entity.DecidedBy is not null)){observed=true;delegation.Live=false;}};
        var body=new WorkDecisionRequestV1("1",false,"SENTETIK rollback",Guid.NewGuid());
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>host.DecideAsync(WorkDecisionHostV1.SupplierReturnCostException,id,body,"supplier-work-rollback",default))).Status);
        Assert.True(observed);Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(1L,await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id",("@id",id)));
        Assert.Equal(1,await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE ReturnId=@id",("@id",id)));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'purchasing.supplier-return.cost-exception-rejected.v1' AND EntityId=@id",("@id",id.ToString("D"))));
        authority.BeforeRead=null;delegation.Live=true;
        Assert.Equal("Rejected",(await host.DecideAsync(WorkDecisionHostV1.SupplierReturnCostException,id,body,"supplier-work-rollback",default)).State);
        Assert.True((await host.DecideAsync(WorkDecisionHostV1.SupplierReturnCostException,id,body,"supplier-work-rollback",default)).Replayed);
    }

    // Reuse the actual SQL fixture, native service/store, same scoped audit writer, Identity grants and owner source.
    // Delegation availability alone is controlled; this is not hosted Identity/Work end-to-end acceptance or SQL race proof.
    private static WorkDecisionHostV1 SupplierReturnPurposeHost(IServiceProvider services,Guid actor,SupplierReturnPurposeDelegations delegations,out SupplierReturnPurposeAuthority observed)
    {
        var company=services.GetRequiredService<ICompanyContext>();company.Set(CompanyA);var user=new ReturnDisclosureUser(actor);
        var correlation=services.GetRequiredService<ICorrelationContext>();correlation.Set("SENTETIK-supplier-work",null,SourceChannels.Web,null,null);
        observed=new(new ErpAuthorizationService(services.GetRequiredService<IdentityDbContext>(),user));
        var execution=new WorkDecisionExecutionFenceV1();var authority=new WorkDecisionAuthorityV1(observed,execution);
        var service=new PurchasingService(company,user,authority,correlation,services.GetRequiredService<IPurchasingPartyAccountPortV1>(),services.GetService<IProductPurchaseEligibilityPortV1>(),
            services.GetRequiredService<PurchaseUnitVerifier>(),services.GetRequiredService<IPurchasingStore>(),new PurchasingServiceBindings(),TimeProvider.System,
            returnStore:services.GetRequiredService<IPurchaseSupplierReturnStore>());
        var source=new PurchaseSupplierReturnCostExceptionWorkInboxSourceV1(services.GetRequiredService<PurchasingDbContext>(),new PurchasingWorkInboxFence(company,user,authority));
        return new(company,user,correlation,authority,[source],delegations,execution,null!,null!,supplierReturns:service);
    }
    private sealed class SupplierReturnPurposeAuthority(IErpAuthorityRevalidationService inner):IErpAuthorityRevalidationService
    {
        public Action? BeforeRead;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid company,CancellationToken token=default)
        {BeforeRead?.Invoke();return inner.RevalidatePermissionScopeAsync(permission,company,token);}
    }
    private sealed class SupplierReturnPurposeDelegations:IWorkDelegationReaderV1
    {
        public bool Live=true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken token=default)=>
            Task.FromResult<WorkDelegationViewV1?>(Live?new(id,CompanyA,item.Kind,PurchasingPermissionManifestV1.OrdersApprove,Guid.NewGuid(),Guid.NewGuid(),AcceptanceTime,DateTimeOffset.UtcNow.AddDays(1),"1",Guid.NewGuid(),AcceptanceTime,"SENTETIK",null,null,null):null);
    }
}
