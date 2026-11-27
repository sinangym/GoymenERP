using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class SupplierReturnWorkDecisionSourceTests
{
    private static readonly DateTimeOffset At=new(2026,11,27,0,0,0,TimeSpan.Zero);
    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public async Task Purpose_reads_pending_and_terminal_by_company_without_parent_read_or_price_copermission(bool terminal,bool prices)
    {
        var world=new PurchasingWorkInboxTestWorld(); if(!prices)world.Withheld.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var (order,row)=Rows();if(terminal){row.Status=(int)PurchaseSupplierReturnStatus.Rejected;row.Version=2;row.DecidedBy=Approver;row.DecidedAtUtc=At.AddHours(1);row.DecisionReason="SENTETIK reject";}
        await world.SeedAsync(CompanyA,db=>{db.Orders.Add(order);db.SupplierReturns.Add(row);});
        var source=new PurchaseSupplierReturnCostExceptionWorkInboxSourceV1(world.Context(CompanyA),world.Fence(CompanyA));
        var answer=await source.ReadDecisionAsync(world.Caller(CompanyA),row.ReturnId,default);Assert.NotNull(answer);Assert.Equal(!terminal,answer.Pending);
        var proof=Assert.IsType<WorkSupplierReturnCostExceptionProofV1>(answer.SupplierReturnCostExceptionProof);
        Assert.Equal(("3","7","1"),(proof.RecordedExpectedPurchaseOrderVersion,proof.CurrentPurchaseOrderVersion,proof.UndoQuantity));
        Assert.Equal(prices?"12.3456":null,answer.Item.Rules[0].Actual);Assert.DoesNotContain(PurchasingPermissionManifestV1.OrdersRead,world.AskedPermissions);
        Assert.DoesNotContain("supplierReturnCostExceptionProof",JsonSerializer.Serialize(answer,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Null(await source.ReadDecisionAsync(world.Caller(CompanyB),row.ReturnId,default));
    }
    [Theory]
    [InlineData("maker")] [InlineData("order")] [InlineData("status")] [InlineData("version")] [InlineData("undo")]
    public async Task Malformed_owned_facts_never_form_a_purpose_proof(string fault)
    {
        var world=new PurchasingWorkInboxTestWorld();var (order,row)=Rows();switch(fault){case "maker":row.RequestedBy=" ";break;case "order":row.OrderId=Guid.NewGuid();break;case "status":row.Status=99;break;case "version":row.Version=3;break;default:row.UndoQuantity=null;break;}
        await world.SeedAsync(CompanyA,db=>{db.Orders.Add(order);db.SupplierReturns.Add(row);});
        var source=new PurchaseSupplierReturnCostExceptionWorkInboxSourceV1(world.Context(CompanyA),world.Fence(CompanyA));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>source.ReadDecisionAsync(world.Caller(CompanyA),row.ReturnId,default));
    }
    [Fact]
    public async Task Late_companywide_permission_loss_does_not_return_owner_facts()
    {
        var world=new PurchasingWorkInboxTestWorld();var(order,row)=Rows();await world.SeedAsync(CompanyA,db=>{db.Orders.Add(order);db.SupplierReturns.Add(row);});
        var reads=0;world.BeforePermissionAnswer=permission=>{if(permission==PurchasingPermissionManifestV1.OrdersApprove && ++reads==2)world.CompanyWide=false;};
        Assert.Null(await new PurchaseSupplierReturnCostExceptionWorkInboxSourceV1(world.Context(CompanyA),world.Fence(CompanyA)).ReadDecisionAsync(world.Caller(CompanyA),row.ReturnId,default));
    }
    private static (PurchaseOrderRow,PurchaseSupplierReturnRow) Rows()
    {
        var order=new PurchaseOrderRow{Id=Guid.NewGuid(),CompanyId=CompanyA,SupplierPartyId=Guid.NewGuid(),CompanyAccountId=Guid.NewGuid(),Status=(int)PurchaseOrderStatus.Approved,Version=7,RowVersion=[1],EffectiveOn=new(2026,11,1),CreatedAtUtc=At,UpdatedAtUtc=At,CreatedBy=Maker,CorrelationId="SENTETIK",DocumentNumber="SENTETIK-PO"};
        return(order,new(){CompanyId=CompanyA,ReturnId=Guid.NewGuid(),ReturnLineId=Guid.NewGuid(),OrderId=order.Id,LineId=Guid.NewGuid(),GoodsAcceptanceId=Guid.NewGuid(),MovementId=Guid.NewGuid(),Status=(int)PurchaseSupplierReturnStatus.PendingCostException,Version=1,RowVersion=[1],SourceBlock="Free",QuantityKg=1,CostBasis=(int)PurchaseSupplierReturnCostBasis.ManualCostWithException,UnitCostTryPerKg=12.3456m,ExpectedPurchaseOrderVersion=3,UndoQuantity=1,Reason="SENTETIK return",OccurredAtUtc=At,RequestedBy=Maker,RequestedAtUtc=At,CorrelationId="SENTETIK"});
    }
}
