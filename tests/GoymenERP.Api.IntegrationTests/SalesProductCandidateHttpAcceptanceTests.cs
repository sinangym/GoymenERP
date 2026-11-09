using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SalesProductCandidateHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly DateOnly EffectiveOn = new(2026, 9, 22);
    private static readonly Guid ProductId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid UnitId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid ProfileId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task Authorized_consumer_gets_bounded_owner_candidates_with_private_no_store()
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(new([
            new(ProductId, "7", "PRD-7", "Bant", [new(UnitId, "kg", ProfileId, "3")])
        ], true));
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(SalesProductCandidateEndpoints.Path
            + "?q=%20BANT%20&effectiveOn=2026-09-22&limit=12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(new ProductSalesCandidateQueryV1(" BANT ", EffectiveOn, 12), Assert.Single(owner.Queries));
        Assert.True(authority.FreshCalls >= 2);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("searchWindowFull").GetBoolean());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("7", item.GetProperty("productVersion").GetString());
        Assert.Equal("kg", Assert.Single(item.GetProperty("orderUnits").EnumerateArray()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_consumer_permission_refuses_before_owner_read()
    {
        var authority = new ControlledAuthority { Granted = false };
        var owner = new StubCandidates(new([], false));
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(SalesProductCandidateEndpoints.Path + "?effectiveOn=2026-09-22");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(owner.Queries);
    }

    [Fact]
    public async Task Cached_consumer_grant_revoked_after_owner_read_is_freshly_refused_without_items()
    {
        var authority = new ControlledAuthority();
        Assert.True(await authority.HasPermissionAsync(
            SalesPermissionManifestV1.DraftsCreate,
            TestCompanyIds.CompanyA));
        var owner = new StubCandidates(new([
            new(ProductId, "7", "PRIVATE-CODE", "PRIVATE-NAME", [new(UnitId, "kg", ProfileId, "3")])
        ], false)) { During = () => authority.Granted = false };
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(SalesProductCandidateEndpoints.Path + "?effectiveOn=2026-09-22");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(1, authority.CachedCalls);
        Assert.True(authority.FreshCalls >= 2);
        Assert.DoesNotContain("PRIVATE-", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validation", 400, "validation_failed")]
    [InlineData("unavailable", 503, "service_unavailable")]
    [InlineData("settings-unavailable", 503, "service_unavailable")]
    public async Task Owner_failures_are_mapped_to_the_public_sales_boundary(string failure, int status, string code)
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(new([], false)) { Failure = failure };
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(SalesProductCandidateEndpoints.Path + "?effectiveOn=2026-09-22");

        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private_owner_detail", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_settings_access_refusal_is_forbidden_without_detail()
    {
        // B-31: a Settings unit-profile refusal let through by the owner's eligibility is 403, never an unmapped 500.
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(new([], false)) { Failure = "settings-denied" };
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(SalesProductCandidateEndpoints.Path + "?effectiveOn=2026-09-22");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.DoesNotContain("settings_access_denied", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_subcontract_picker_gets_only_the_candidates_whose_product_card_is_a_service_and_an_unreadable_type_is_503()
    {
        // SAT-D176 (EK25): `itemType=Service` keeps the owner's window but only the service products (the shared card, SAT-D149).
        var service = Guid.Parse("71717171-7171-4171-8171-717171717171");
        var owner = new StubCandidates(new([
            new(ProductId, "7", "PRD-7", "Bant", [new(UnitId, "kg", ProfileId, "3")]),
            new(service, "2", "HZM-1", "Sentetik işçilik", [new(UnitId, "kg", ProfileId, "3")]),
        ], true));
        var itemTypes = new StubItemTypes { [ProductId] = new(ProductItemTypeOutcomeV1.Found, ProductItemTypesV1.Steel, 1),
            [service] = new(ProductItemTypeOutcomeV1.Found, ProductItemTypesV1.Service, 1) };
        using var host = CreateHost(new ControlledAuthority(), owner, itemTypes);
        using var client = CreateClient(host);

        using (var response = await client.GetAsync(SalesProductCandidateEndpoints.Path + "?effectiveOn=2026-09-22&itemType=Service"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(document.RootElement.GetProperty("searchWindowFull").GetBoolean());
            Assert.Equal("HZM-1", Assert.Single(document.RootElement.GetProperty("items").EnumerateArray()).GetProperty("code").GetString());
        }

        using (var unknown = await client.GetAsync(SalesProductCandidateEndpoints.Path + "?effectiveOn=2026-09-22&itemType=Hizmet"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        }

        itemTypes[service] = new(ProductItemTypeOutcomeV1.Unavailable, null, null);
        using (var unavailable = await client.GetAsync(SalesProductCandidateEndpoints.Path + "?effectiveOn=2026-09-22&itemType=Service"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.DoesNotContain("HZM-1", await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    private WebApplicationFactory<Program> CreateHost(ControlledAuthority authority, StubCandidates owner, StubItemTypes? itemTypes = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<IProductSalesCandidatesV1>();
            services.AddSingleton<IProductSalesCandidatesV1>(owner);
            if (itemTypes is not null)
            {
                services.RemoveAll<IProductItemTypeReaderV1>();
                services.AddSingleton<IProductItemTypeReaderV1>(itemTypes);
            }
        }));

    private sealed class StubItemTypes : Dictionary<Guid, ProductItemTypeReadV1>, IProductItemTypeReaderV1
    {
        public Task<ProductItemTypeReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TryGetValue(productId, out var answer) ? answer : new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.NotFound, null, null));
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubCandidates(ProductSalesCandidatesV1 result) : IProductSalesCandidatesV1
    {
        public List<ProductSalesCandidateQueryV1> Queries { get; } = [];
        public Action? During { get; init; }
        public string? Failure { get; init; }

        public Task<ProductSalesCandidatesV1> SearchAsync(ProductSalesCandidateQueryV1 query,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);
            During?.Invoke();
            return Failure switch
            {
                "validation" => Task.FromException<ProductSalesCandidatesV1>(
                    new ProductValidationException([new("q", "private_owner_detail")])),
                "unavailable" => Task.FromException<ProductSalesCandidatesV1>(
                    new ProductOperationException("private_owner_detail", 503)),
                // B-31: the owner's eligibility lets the Settings unit-profile refusal through as is.
                "settings-unavailable" => Task.FromException<ProductSalesCandidatesV1>(
                    new SettingsPrerequisiteException("private_owner_detail")),
                "settings-denied" => Task.FromException<ProductSalesCandidatesV1>(
                    new SettingsPrerequisiteException("settings_access_denied")),
                _ => Task.FromResult(result)
            };
        }
    }

    private sealed class ControlledAuthority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        private bool? _cachedGrant;
        public bool Granted { get; set; } = true;
        public int CachedCalls { get; private set; }
        public int FreshCalls { get; private set; }

        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            CachedCalls++;
            _cachedGrant ??= ReadGrant(permission, companyId);
            return Task.FromResult(_cachedGrant.Value);
        }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            FreshCalls++;
            _cachedGrant = ReadGrant(permission, companyId);
            return Task.FromResult(_cachedGrant.Value
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }

        private bool ReadGrant(string permission, Guid companyId) => Granted
            && permission == SalesPermissionManifestV1.DraftsCreate
            && companyId == TestCompanyIds.CompanyA;

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
