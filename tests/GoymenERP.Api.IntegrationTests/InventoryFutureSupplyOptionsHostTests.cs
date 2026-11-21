using GoymenERP.Api.InventoryRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Owner adapters are doubles; these facts do not claim SQL, browser or source eligibility acceptance.</summary>
public sealed class InventoryFutureSupplyOptionsHostTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Identity = Guid.Parse("8a210000-0000-4000-8000-000000000002");
    private static readonly Guid Supplier = Guid.Parse("8a210000-0000-4000-8000-000000000003");

    [Fact]
    public async Task Source_choices_display_only_owner_suppliers_and_units_and_preserve_exact_available_quantity()
    {
        var world = new Sources();
        var result = await world.Host().ReadAsync(Identity, Identity, null);
        var option = Assert.Single(result.Items);
        Assert.Equal(("Sentetik tedarikçi", "kg", "50.001", "10", "40.001"),
            (option.SupplierName, option.UnitCode, option.AllocatableQuantity, option.ReservedQuantity, option.AvailableQuantity));
        Assert.Equal([Supplier], world.DisplayIds);
        Assert.Equal(world.UserId, world.DisplayActor);
        Assert.Equal([ErpPermissions.InventoryFutureAllocationWrite, ErpPermissions.InventoryFutureAllocationWrite], world.Permissions);
        Assert.Equal((Company, Identity, "100"), (result.CompanyId, result.ProductId, result.OpenDemandQuantity));
    }

    [Fact]
    public async Task The_http_choice_read_uses_the_narrow_write_permission_and_rejects_unknown_query_fields()
    {
        var world = new Sources();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInventoryFutureDemandSourceV1>(); services.AddSingleton<IInventoryFutureDemandSourceV1>(world);
            services.RemoveAll<IPurchaseFutureSupplyReadPortV1>(); services.AddSingleton<IPurchaseFutureSupplyReadPortV1>(world);
            services.RemoveAll<IInventoryFutureAllocationReadPortV1>(); services.AddSingleton<IInventoryFutureAllocationReadPortV1>(world);
            services.RemoveAll<ICounterpartyPurchasingRecordPartyDisplaySourceV1>(); services.AddSingleton<ICounterpartyPurchasingRecordPartyDisplaySourceV1>(world);
            services.RemoveAll<ISettingsUnitIdentityCatalogV1>(); services.AddSingleton<ISettingsUnitIdentityCatalogV1>(world);
            services.RemoveAll<IErpAuthorizationService>(); services.AddSingleton<IErpAuthorizationService>(new HttpAuthority());
            services.RemoveAll<IErpAuthorityRevalidationService>(); services.AddSingleton<IErpAuthorityRevalidationService>(world);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", Company.ToString("D"));
        var path = $"/api/v1/inventory/sales-orders/{Identity:D}/lines/{Identity:D}/future-supply-options";
        using var invalid = await client.GetAsync(path + "?supplierPartyId=" + Supplier);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Empty(world.DisplayIds);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        var value = await response.Content.ReadFromJsonAsync<InventoryFutureSupplyOptionsV1>();
        Assert.Equal("40.001", Assert.Single(value!.Items).AvailableQuantity);
        Assert.DoesNotContain("price", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Foreign_sources_and_unreadable_allocations_never_become_zero_reserved(bool foreign, bool unreadable)
    {
        var world = new Sources { Foreign = foreign, Unreadable = unreadable };
        var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() => world.Host().ReadAsync(Identity, Identity, null));
        Assert.Equal(503, refused.Status);
        if (foreign) Assert.Empty(world.DisplayIds);
    }

    private sealed class Sources : ICurrentUser, IErpAuthorityRevalidationService, IInventoryFutureDemandSourceV1,
        IPurchaseFutureSupplyReadPortV1, IInventoryFutureAllocationReadPortV1,
        ICounterpartyPurchasingRecordPartyDisplaySourceV1, ISettingsUnitIdentityCatalogV1
    {
        public bool Foreign { get; init; }
        public bool Unreadable { get; init; }
        public IReadOnlyList<Guid> DisplayIds { get; private set; } = [];
        public string? DisplayActor { get; private set; }
        public List<string> Permissions { get; } = [];
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-options";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
        public InventoryFutureSupplyOptionsHost Host()
        {
            var company = new CompanyContext(); company.Set(Company);
            return new(company, this, this, this, this, this, this, this);
        }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Permissions.Add(permission);
            return Task.FromResult(new PermissionScope(companyId == Company && permission == ErpPermissions.InventoryFutureAllocationWrite,
                true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
        public Task<InventoryFutureDemandSourceAnswerV1> GetDemandLineAsync(Guid salesOrderId, Guid salesOrderLineId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.Open,
                new(salesOrderId, salesOrderLineId, Identity, "1", Identity, "100")));
        public Task<PurchaseFutureSupplyAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The list is the only purchase source expected here.");
        public Task<PurchaseFutureSupplyPageV1> ListAllocatableLinesAsync(Guid productId, int pageSize, string? cursor, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PurchaseFutureSupplyPageV1([new(Foreign ? Guid.NewGuid() : Company, Identity, Identity, 1, Supplier,
                Identity, productId, "1", Identity, "General", "50.001", "0", "0", "0", "50.001", null, null, 1)], null));
        public Task<InventoryFutureAllocationSumV1> GetAllocatedOnPurchaseLineAsync(Guid purchaseOrderLineId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Unreadable ? new InventoryFutureAllocationSumV1(InventoryFutureAllocationReadOutcomeV1.Unreadable, null, null)
                : new(InventoryFutureAllocationReadOutcomeV1.Present, "10", Identity));
        public Task<InventoryFutureAllocationPresenceV1> GetConsumedPresenceOnPurchaseLineAsync(Guid purchaseOrderLineId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The display read must not ask for consumed quantities.");
        public Task<CounterpartyPurchasingRecordPartyDisplayAnswerV1> ReadForAuthorizedPurchasingRecordsAsync(
            CounterpartyPurchasingRecordPartyDisplayRequestV1 request, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Company, request.CompanyId);
            Assert.False(string.IsNullOrWhiteSpace(request.ActorId));
            DisplayIds = request.PartyIds;
            DisplayActor = request.ActorId;
            return Task.FromResult(new CounterpartyPurchasingRecordPartyDisplayAnswerV1(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Found,
                [new(Supplier, "SYN", "Sentetik tedarikçi", null)]));
        }
        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UnitIdentityV1?>(new(unitId, "kg", "weight", true));
        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unit reads must be bounded to the owner's returned identities.");
    }
    private sealed class HttpAuthority : IErpAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId).IsGranted);
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId));
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("SENTETIK-options-http", new HashSet<Guid> { Company },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId), StringComparer.Ordinal)));
        private static PermissionScope Scope(string permission, Guid companyId) => new(companyId == Company && permission == ErpPermissions.InventoryFutureAllocationWrite,
            true, new HashSet<string> { "SENTETIK-http" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    }
}
