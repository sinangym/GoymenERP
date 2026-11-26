using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

/// <summary>Negative purpose-read fences only; positive SQL query behavior remains a real SQL acceptance gate.</summary>
public sealed class SalesOrderWorkDecisionReadV1Tests
{
    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("scoped")]
    public async Task A_refused_context_never_touches_the_sales_database(string mismatch)
    {
        var company=new CompanyContext(mismatch=="company"?OtherCompany:Company);
        var authority=new Authority(permission=>mismatch!="permission"&&permission==SalesPermissionManifestV1.OrdersApprove);
        var reader=new SalesOrderWorkDecisionReadV1(null!,company,new User(Approver,Company),authority);
        var context=Context(mismatch=="actor"?Maker:Approver);
        if(mismatch=="scoped")context=context with{GrantScope=new WorkInboxGrantScopeV1(new HashSet<Guid>([Guid.NewGuid()]),new HashSet<Guid>(),new HashSet<Guid>())};
        Assert.Null(await reader.ReadAsync(context,Guid.NewGuid(),default));
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied,(await reader.ListPendingAsync(context,50,default)).Outcome);
        Assert.DoesNotContain(SalesPermissionManifestV1.DraftsRead,authority.Asked);
        Assert.DoesNotContain(SalesPermissionManifestV1.OrdersPriceRead,authority.Asked);
    }
}
