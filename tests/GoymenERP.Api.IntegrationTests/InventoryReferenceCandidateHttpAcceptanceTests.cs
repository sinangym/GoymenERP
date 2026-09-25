using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryReferenceCandidateHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid ProductId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UnitId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ProfileId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid LocationId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid SiteId = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public async Task Authorized_actor_gets_exact_product_and_location_candidates_with_private_no_store()
    {
        var authority = new ControlledAuthority();
        var products = new StubProducts(new([
            new(ProductId, "7", "PRD-7", "Bant", UnitId, "kg", ProfileId, "3")
        ], true));
        var locations = new StubLocations(new([
            new(LocationId, "LOC-1", "Depo", SiteId, "SITE-1", "Merkez")
        ], false));
        using var host = CreateHost(authority, products, locations);
        using var client = CreateClient(host);

        using var productResponse = await client.GetAsync(InventoryReferenceEndpoints.ProductPath
            + "?q=%20BANT%20&effectiveOn=2026-09-24&limit=12");
        using var locationResponse = await client.GetAsync(InventoryReferenceEndpoints.LocationPath
            + "?q=%20DEPO%20&limit=9");

        Assert.Equal(HttpStatusCode.OK, productResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, locationResponse.StatusCode);
        AssertPrivateNoStore(productResponse);
        AssertPrivateNoStore(locationResponse);
        Assert.Equal(new ProductStockCandidateQueryV1(" BANT ", new DateOnly(2026, 9, 24), 12),
            Assert.Single(products.Queries));
        var locationCall = Assert.Single(locations.Queries);
        Assert.Equal(TestCompanyIds.CompanyA, locationCall.CompanyId);
        Assert.Equal(new OrganizationLocationCandidateQueryV1(" DEPO ", 9), locationCall.Query);
        Assert.True(authority.FreshCalls >= 4);
        Assert.Contains("PRD-7", await productResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("LOC-1", await locationResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("product", "?effectiveOn=2026-09-24&unknown=x")]
    [InlineData("product", "?effectiveOn=2026-09-24&q=a&q=b")]
    [InlineData("product", "?effectiveOn=24-09-2026")]
    [InlineData("product", "?effectiveOn=2026-09-24&limit=101")]
    [InlineData("location", "?q=a&q=b")]
    [InlineData("location", "?limit=abc")]
    public async Task Invalid_query_is_coded_400_before_owner_read(string target, string query)
    {
        var authority = new ControlledAuthority();
        var products = new StubProducts(new([], false));
        var locations = new StubLocations(new([], false));
        using var host = CreateHost(authority, products, locations);
        using var client = CreateClient(host);
        var path = target == "product" ? InventoryReferenceEndpoints.ProductPath : InventoryReferenceEndpoints.LocationPath;

        using var response = await client.GetAsync(path + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertPrivateNoStore(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(target == "product" ? InventoryReferenceCodesV1.ProductInvalidQuery
                : InventoryReferenceCodesV1.LocationInvalidQuery,
            body.RootElement.GetProperty("code").GetString());
        Assert.Empty(products.Queries);
        Assert.Empty(locations.Queries);
    }

    [Fact]
    public async Task Missing_consumer_permission_refuses_before_owner_read()
    {
        var authority = new ControlledAuthority { Granted = false };
        var products = new StubProducts(new([], false));
        var locations = new StubLocations(new([], false));
        using var host = CreateHost(authority, products, locations);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryReferenceEndpoints.ProductPath + "?effectiveOn=2026-09-24");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(products.Queries);
    }

    [Fact]
    public async Task Revocation_after_owner_read_is_code_less_403_without_candidate_disclosure()
    {
        var authority = new ControlledAuthority();
        var products = new StubProducts(new([
            new(ProductId, "7", "PRIVATE-CODE", "PRIVATE-NAME", UnitId, "kg", ProfileId, "3")
        ], false)) { During = () => authority.Granted = false };
        using var host = CreateHost(authority, products, new StubLocations(new([], false)));
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryReferenceEndpoints.ProductPath + "?effectiveOn=2026-09-24");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PRIVATE-", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("product-invalid", 400, InventoryReferenceCodesV1.ProductInvalidQuery)]
    [InlineData("product-conflict", 409, InventoryReferenceCodesV1.ProductReadConflict)]
    [InlineData("product-unavailable", 503, "service_unavailable")]
    [InlineData("location-invalid", 400, InventoryReferenceCodesV1.LocationInvalidQuery)]
    [InlineData("location-conflict", 409, InventoryReferenceCodesV1.LocationReadConflict)]
    public async Task Owner_failures_are_mapped_to_the_closed_public_boundary(
        string failure, int status, string code)
    {
        var authority = new ControlledAuthority();
        var products = new StubProducts(new([], false)) { Failure = failure };
        var locations = new StubLocations(new([], false)) { Failure = failure };
        using var host = CreateHost(authority, products, locations);
        using var client = CreateClient(host);
        var path = failure.StartsWith("product", StringComparison.Ordinal)
            ? InventoryReferenceEndpoints.ProductPath + "?effectiveOn=2026-09-24"
            : InventoryReferenceEndpoints.LocationPath;

        using var response = await client.GetAsync(path);

        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private_owner_detail", text, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateHost(
        ControlledAuthority authority, StubProducts products, StubLocations locations) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<IProductStockCandidatesV1>();
            services.AddSingleton<IProductStockCandidatesV1>(products);
            services.RemoveAll<IOrganizationLocationCandidatesV1>();
            services.AddSingleton<IOrganizationLocationCandidatesV1>(locations);
        }));

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

    private sealed class StubProducts(ProductStockCandidatesV1 result) : IProductStockCandidatesV1
    {
        public List<ProductStockCandidateQueryV1> Queries { get; } = [];
        public string? Failure { get; init; }
        public Action? During { get; init; }

        public Task<ProductStockCandidatesV1> SearchAsync(ProductStockCandidateQueryV1 query,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);
            During?.Invoke();
            return Failure switch
            {
                "product-invalid" => Task.FromException<ProductStockCandidatesV1>(
                    new ProductValidationException([new("q", "private_owner_detail")])),
                "product-conflict" => Task.FromException<ProductStockCandidatesV1>(
                    new InvalidOperationException("private_owner_detail")),
                "product-unavailable" => Task.FromException<ProductStockCandidatesV1>(
                    new ProductOperationException("private_owner_detail", 503)),
                _ => Task.FromResult(result),
            };
        }
    }

    private sealed class StubLocations(OrganizationLocationCandidatesV1 result)
        : IOrganizationLocationCandidatesV1
    {
        public List<(Guid CompanyId, OrganizationLocationCandidateQueryV1 Query)> Queries { get; } = [];
        public string? Failure { get; init; }

        public Task<OrganizationLocationCandidatesV1> SearchAsync(Guid companyId,
            OrganizationLocationCandidateQueryV1 query, CancellationToken cancellationToken = default)
        {
            Queries.Add((companyId, query));
            return Failure switch
            {
                "location-invalid" => Task.FromException<OrganizationLocationCandidatesV1>(
                    new OrganizationLocationCandidateQueryException()),
                "location-conflict" => Task.FromException<OrganizationLocationCandidatesV1>(
                    new InvalidOperationException("private_owner_detail")),
                _ => Task.FromResult(result),
            };
        }
    }

    private sealed class ControlledAuthority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public int FreshCalls { get; private set; }

        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Granted && permission == ErpPermissions.InventoryOpeningStockRecord
                && companyId == TestCompanyIds.CompanyA);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            FreshCalls++;
            return Task.FromResult(Granted && permission == ErpPermissions.InventoryOpeningStockRecord
                && companyId == TestCompanyIds.CompanyA
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
