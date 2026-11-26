using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesInvoiceWorkDecisionSourceV1Tests
{
    private static readonly Guid Id = Guid.Parse("5a1e0000-0000-4000-8000-000000000081");
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Purpose_read_preserves_maker_submitter_and_real_version_after_the_pending_phase(bool pending)
    {
        var store = new Store(pending);
        var authority = new Authority(permission => permission is SalesPermissionManifestV1.InvoicesApprove or SalesPermissionManifestV1.OrdersPriceRead);
        IWorkApprovalDecisionSourceV1 source = new SalesInvoiceWorkInboxSourceV1(store,new CompanyContext(Company),new User(Approver,Company),authority);
        var result = await source.ReadDecisionAsync(Context(),Id,default);
        Assert.NotNull(result); Assert.Equal(pending,result.Pending); Assert.Equal("9",result.SourceVersion);
        Assert.Equal(Maker,result.Item.PreparedBy); Assert.True(result.Item.Excludes("SENTETIK-submitter"));
        Assert.DoesNotContain(SalesPermissionManifestV1.DraftsRead,authority.Asked);
        Assert.DoesNotContain(SalesPermissionManifestV1.InvoicesRead,authority.Asked);
    }
    [Fact]
    public async Task Missing_price_co_permission_does_not_read_the_owner_record()
    {
        var store=new Store(true);
        IWorkApprovalDecisionSourceV1 source=new SalesInvoiceWorkInboxSourceV1(store,new CompanyContext(Company),new User(Approver,Company),
            new Authority(permission=>permission==SalesPermissionManifestV1.InvoicesApprove));
        Assert.Null(await source.ReadDecisionAsync(Context(),Id,default)); Assert.Equal(0,store.Reads);
    }
    [Fact]
    public async Task Authority_loss_during_the_read_withholds_the_snapshot()
    {
        var store=new Store(true); var grants=0;
        var authority=new Authority(permission=>permission!=SalesPermissionManifestV1.InvoicesApprove||++grants==1);
        IWorkApprovalDecisionSourceV1 source=new SalesInvoiceWorkInboxSourceV1(store,new CompanyContext(Company),new User(Approver,Company),authority);
        Assert.Null(await source.ReadDecisionAsync(Context(),Id,default)); Assert.Equal(1,store.Reads);
    }
    private sealed class Store(bool pending):ISalesWorkInboxApprovalStoreV1
    {
        public int Reads { get; private set; }
        public Task<SalesWorkInboxInvoiceDecisionV1?> FindInvoiceDecisionAsync(Guid companyId,Guid invoiceId,CancellationToken cancellationToken)
        {
            Reads++; Assert.Equal(Company,companyId); Assert.Equal(Id,invoiceId);
            return Task.FromResult<SalesWorkInboxInvoiceDecisionV1?>(new(new(Id,Company,"SENTETIK invoice",Maker,"SENTETIK-submitter",T0),9,pending));
        }
        public Task<IReadOnlyList<SalesWorkInboxRequestV1>> ListSubmittedRequestsAsync(Guid companyId,int take,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyList<SalesWorkInboxInvoiceV1>> ListPendingInvoicesAsync(Guid companyId,int take,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyList<SalesWorkInboxInvoiceV1>> ListPendingSubcontractInvoicesAsync(Guid companyId,int take,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyList<SalesWorkInboxCustomerReturnV1>> ListPendingCustomerReturnsAsync(Guid companyId,int take,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyList<SalesWorkInboxContractV1>> ListPendingDiscountContractsAsync(Guid companyId,int take,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyList<SalesWorkInboxQuoteConversionRequestV1>> ListPendingQuoteConversionRequestsAsync(Guid companyId,int take,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyList<SalesWorkInboxOverDeliveryRequestV1>> ListPendingOverDeliveryRequestsAsync(Guid companyId,int take,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }
}
