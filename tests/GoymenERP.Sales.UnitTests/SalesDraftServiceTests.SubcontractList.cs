using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Subcontract_invoice_metadata_selection_requires_read_but_not_write_or_price_permission()
    {
        var fixture = await SubcontractInvoicing.CreateAsync();
        fixture.World.Authority.Denied.Add(SalesPermissionManifestV1.InvoicesWrite);
        fixture.World.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
        var store = new SubcontractListStore(fixture.Order.Draft.Id);
        var result = await SubcontractListService(fixture, store).ListSubcontractInvoicesAsync(fixture.Order.Draft.Id, new());
        Assert.Equal(fixture.Order.Draft.Id, result.SalesOrderId);
        Assert.Single(result.Items);
        Assert.Equal(1, store.Reads);
    }

    [Theory]
    [InlineData("denied", 403, 0)]
    [InlineData("lost", 403, 1)]
    [InlineData("missing-order", 404, 0)]
    [InlineData("wrong-order-response", 503, 1)]
    public async Task Subcontract_invoice_metadata_selection_fences_authority_and_order_sources(string fault, int status, int reads)
    {
        var fixture = await SubcontractInvoicing.CreateAsync();
        var store = new SubcontractListStore(fault == "wrong-order-response" ? Guid.NewGuid() : fixture.Order.Draft.Id);
        if (fault == "denied") fixture.World.Authority.Denied.Add(SalesPermissionManifestV1.InvoicesRead);
        if (fault == "lost") store.OnRead = () => fixture.World.Authority.Denied.Add(SalesPermissionManifestV1.InvoicesRead);
        var id = fault == "missing-order" ? Guid.NewGuid() : fixture.Order.Draft.Id;
        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => SubcontractListService(fixture, store).ListSubcontractInvoicesAsync(id, new()));
        Assert.Equal(status, error.Status);
        Assert.Equal(reads, store.Reads);
    }

    private static SalesInvoiceService SubcontractListService(SubcontractInvoicing fixture, ISalesSubcontractInvoiceListStoreV1 store)
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(company, fixture.World.User, fixture.World.Authority, new FixedCorrelation(), new FakeInvoiceStore(),
            fixture.World.Store, fixture.World.Settings, fixture.World.Clock, subcontractInvoiceList: store);
    }

    private sealed class SubcontractListStore(Guid orderId) : ISalesSubcontractInvoiceListStoreV1
    {
        public int Reads { get; private set; }
        public Action? OnRead { get; set; }
        public Task<SalesSubcontractInvoiceListV1> ListSubcontractInvoicesAsync(Guid salesOrderId, int pageSize, string? cursor,
            CancellationToken cancellationToken = default)
        {
            Reads++; OnRead?.Invoke();
            return Task.FromResult(new SalesSubcontractInvoiceListV1(orderId,
                [new(Guid.NewGuid(), "SFH-SENTETIK", "Draft", "1", Day, DateTimeOffset.UnixEpoch)], null));
        }
    }
}
