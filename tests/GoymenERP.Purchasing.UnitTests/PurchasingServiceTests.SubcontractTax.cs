using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Purchasing_tax_override_reads_actual_list_and_default_versions_and_uses_only_the_existing_Purchasing_approval_authority()
    {
        var source = new ServiceCommandSource(); var store = new ServiceCommandStore(); var catalog = new ServiceTaxCatalog();
        var draft = ServiceTaxState(source); store.Seed(draft); var authority = new FixedAuthority();
        authority.Denied.Add(ErpPermissions.SalesOrdersApprove); authority.Denied.Add(ErpPermissions.PurchasingOrdersRevise);
        var service = new PurchaseServiceOrderServiceV1(new MutableCompanyContext(), new FixedUser(), authority, store,
            source, source, source, source, TimeProvider.System, taxCatalog: catalog);
        var request = new RevisePurchaseServiceOrderTaxV1("1", draft.Lines[0].LineId, false, "1", "SENTETIK-lists:1", "10", null, null, "SENTETIK vergi");
        var result = await service.ReviseTaxAsync(draft.OrderId, request, new("SENTETIK-tax-owner", "SENTETIK-correlation", null, DateTimeOffset.UtcNow));
        Assert.Equal("2", result.Order.Version); Assert.Equal(10m, store.Saved!.Lines[0].Tax!.VatPercent);
        Assert.Equal("SENTETIK-lists:1", store.Saved.Lines[0].Tax!.Change!.SourceVersion);
        Assert.Contains(PurchasingPermissionManifestV1.OrdersApprove, authority.Measured);
        Assert.DoesNotContain(ErpPermissions.SalesOrdersApprove, authority.Measured);
        Assert.Equal(draft.Materials, store.Saved.Materials); Assert.Equal(draft.Lines[0].UnitPrice, store.Saved.Lines[0].UnitPrice);
    }
    [Fact]
    public async Task A_stale_tax_list_or_missing_Purchasing_approval_never_changes_the_order()
    {
        var source = new ServiceCommandSource(); var store = new ServiceCommandStore(); var catalog = new ServiceTaxCatalog();
        var draft = ServiceTaxState(source); store.Seed(draft); var authority = new FixedAuthority();
        var service = new PurchaseServiceOrderServiceV1(new MutableCompanyContext(), new FixedUser(), authority, store,
            source, source, source, source, TimeProvider.System, taxCatalog: catalog);
        var request = new RevisePurchaseServiceOrderTaxV1("1", draft.Lines[0].LineId, false, "1", "SENTETIK-old", "10", null, null, "SENTETIK");
        Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.ReviseTaxAsync(draft.OrderId, request,
            new("SENTETIK-tax-stale", "SENTETIK-correlation", null, DateTimeOffset.UtcNow)))).Status);
        Assert.Same(draft, store.Saved);
        authority.Denied.Add(PurchasingPermissionManifestV1.OrdersApprove);
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.ReviseTaxAsync(draft.OrderId,
            request with { ExpectedSourceVersion = "SENTETIK-lists:1" }, new("SENTETIK-tax-deny", "SENTETIK-correlation", null, DateTimeOffset.UtcNow)))).Status);
        Assert.Same(draft, store.Saved);
    }
    private static PurchaseServiceOrderState ServiceTaxState(ServiceCommandSource source)
    {
        var now = DateTimeOffset.UtcNow;
        return PurchaseServiceOrder.Create(new(Company, Guid.NewGuid(), 1, PurchaseServiceOrderKind.OwnMaterialSubcontract,
            PurchaseServiceOrderStatus.Draft, Party, Account, "1", "1", Day, Actor, null, now, now,
            new(source.Currency, "TRY", Guid.NewGuid(), 1, 2, 4),
            [new(Guid.NewGuid(), source.ServiceType, 1, "SENTETIK hizmet", KgUnit, "kg", KgProfile, 1, 3, 10m, 2m, 0m,
                new(true, 20m, null, false, null, null, 1))],
            [new(Guid.NewGuid(), "1", Product, "1", 10m)], [new(Product, "1", 9m)], []));
    }
    private sealed class ServiceTaxCatalog : ISettingsSubcontractTaxCatalogV1
    {
        public Task<SubcontractTaxOptionsV1> GetAsync(Guid company, SubcontractTaxSideV1 side, DateOnly on, CancellationToken token = default) =>
            Task.FromResult(new SubcontractTaxOptionsV1(company, side, on, "1", new(true, "20", null, false, null, null),
                [new("10", "SENTETIK KDV", "10", null)], [], [], [], "SENTETIK-lists:1"));
    }
}
