using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryStockPositionHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid LabelId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ProductId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid LotId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid LocationId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public async Task Authorized_actor_gets_exact_positions_with_private_no_store()
    {
        var authority = new ControlledAuthority();
        var owner = new StubPositions(Positions());
        var product = new StubProductDisplays(ProductDisplays());
        using var host = CreateHost(authority, owner, product);
        using var client = CreateClient(host);

        using var request = ProductDisplayRequest(InventoryStockPositionApiV1.PositionsPath
            + $"?q=%20ROLL-1%20&limit=12&cursor=opaque&productId={ProductId:D}"
            + $"&locationId={LocationId:D}&classification=Standard&ownerKind=Company"
            + $"&ownerId={TestCompanyIds.CompanyA:D}");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(new InventoryStockPositionQueryV1(" ROLL-1 ", 12, "opaque", ProductId,
            LocationId, InventoryLabelClassificationV1.Standard, "Company", TestCompanyIds.CompanyA),
            Assert.Single(owner.Queries));
        Assert.Equal(new ProductStockDisplaySelectionV1(ProductId, "7"),
            Assert.Single(Assert.Single(product.Queries).Selections));
        Assert.Contains(ErpPermissions.InventoryLabelsRead, authority.CheckedPermissions);
        Assert.Contains(ErpPermissions.InventoryBalancesRead, authority.CheckedPermissions);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("searchWindowFull").GetBoolean());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(LabelId, item.GetProperty("labelId").GetGuid());
        Assert.Equal("3", item.GetProperty("labelVersion").GetString());
        Assert.Equal("7", item.GetProperty("availableKg").GetString());
        var display = item.GetProperty("productDisplay");
        Assert.Equal("P-1", display.GetProperty("identity").GetProperty("code").GetString());
        Assert.Equal(1.5m, display.GetProperty("technical").GetProperty("thicknessMm").GetDecimal());
    }

    [Fact]
    public async Task Location_display_contract_batches_the_distinct_page_locations_once_and_returns_owner_identity()
    {
        var product = new StubProductDisplays(ProductDisplays());
        var locations = new StubLocationDisplays(LocationDisplays());
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(Positions()), product, locations);
        using var client = CreateClient(host);

        using var request = LocationDisplayRequest(InventoryStockPositionApiV1.PositionsPath);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([LocationId], Assert.Single(locations.Queries).LocationIds);
        Assert.Equal(TestCompanyIds.CompanyA, Assert.Single(locations.Queries).CompanyId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("P-1", item.GetProperty("productDisplay").GetProperty("identity").GetProperty("code").GetString());
        var location = item.GetProperty("locationDisplay");
        Assert.Equal(LocationId, location.GetProperty("locationId").GetGuid());
        Assert.Equal("DEP-01", location.GetProperty("locationCode").GetString());
        Assert.Equal("Ana Depo", location.GetProperty("locationName").GetString());
    }

    [Fact]
    public async Task Product_display_1_0_keeps_its_shape_and_does_not_call_Organization()
    {
        var locations = new StubLocationDisplays(LocationDisplays());
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(Positions()),
            locations: locations);
        using var client = CreateClient(host);

        using var request = ProductDisplayRequest(InventoryStockPositionApiV1.PositionsPath);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(locations.Queries);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(item.TryGetProperty("productDisplay", out _));
        Assert.False(item.TryGetProperty("locationDisplay", out _));
    }

    [Fact]
    public async Task Empty_location_display_page_skips_both_display_owner_reads()
    {
        var product = new StubProductDisplays(ProductDisplays());
        var locations = new StubLocationDisplays(LocationDisplays());
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(new([], false)), product, locations);
        using var client = CreateClient(host);

        using var request = LocationDisplayRequest(InventoryStockPositionApiV1.PositionsPath);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(product.Queries);
        Assert.Empty(locations.Queries);
    }

    [Fact]
    public async Task Client_without_display_contract_gets_the_exact_legacy_shape_and_Product_is_not_called()
    {
        var product = new StubProductDisplays(ProductDisplays());
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(Positions()), product);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(product.Queries);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["items", "searchWindowFull"], document.RootElement.EnumerateObject().Select(property => property.Name));
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain(item.EnumerateObject(), property => property.Name is "productVersion" or "classification" or "productDisplay");
        Assert.Equal("ROLL-1", item.GetProperty("labelCode").GetString());
    }

    [Fact]
    public async Task Either_missing_read_permission_refuses_before_owner_read()
    {
        foreach (var missing in new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead })
        {
            var authority = new ControlledAuthority { MissingPermission = missing };
            var owner = new StubPositions(Positions());
            using var host = CreateHost(authority, owner);
            using var client = CreateClient(host);

            using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            AssertPrivateNoStore(response);
            Assert.Empty(owner.Queries);
            Assert.DoesNotContain("ROLL-1", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Unauthenticated_request_is_private_no_store_and_never_reaches_the_owner()
    {
        var authority = new ControlledAuthority();
        var owner = new StubPositions(Positions());
        using var host = CreateHost(authority, owner);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));

        using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(owner.Queries);
    }

    [Fact]
    public async Task Invalid_query_is_coded_400_before_owner_read()
    {
        var owner = new StubPositions(Positions());
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        foreach (var query in new[] { "?unknown=x", "?q=a&q=b", "?limit=abc", "?limit=101",
                     "?productId=not-a-guid", "?classification=standard", "?ownerId=not-a-guid",
                     "?ownerKind=1", "?ownerKind=2" })
        {
            using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(InventoryStockPositionCodesV1.InvalidQuery,
                document.RootElement.GetProperty("code").GetString());
        }

        Assert.Empty(owner.Queries);
    }

    [Fact]
    public async Task Invalid_or_duplicate_display_contract_is_coded_400_before_owner_read()
    {
        foreach (var values in new[] { new[] { "3.0" }, new[] { "1.0", "1.0" } })
        {
            var owner = new StubPositions(Positions());
            using var host = CreateHost(new ControlledAuthority(), owner);
            using var client = CreateClient(host);
            using var request = new HttpRequestMessage(HttpMethod.Get, InventoryStockPositionApiV1.PositionsPath);
            foreach (var value in values)
                request.Headers.TryAddWithoutValidation(InventoryStockPositionApiV1.ProductDisplayContractHeader, value);

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(owner.Queries);
        }
    }

    [Theory]
    [InlineData(400, InventoryStockPositionCodesV1.InvalidQuery)]
    [InlineData(409, InventoryStockPositionCodesV1.ReadConflict)]
    [InlineData(503, InventoryStockPositionCodesV1.Unavailable)]
    [InlineData(401, InventoryStockPositionCodesV1.Unauthenticated)]
    [InlineData(403, InventoryStockPositionCodesV1.Forbidden)]
    public async Task Owner_refusals_preserve_only_the_public_error_contract(int status, string privateCode)
    {
        var owner = new StubPositions(Positions())
        {
            Failure = new InventoryStockPositionException(status, privateCode),
        };
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var request = ProductDisplayRequest(InventoryStockPositionApiV1.PositionsPath);
        using var response = await client.SendAsync(request);

        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        if (status is 400 or 409)
        {
            Assert.Equal(privateCode, document.RootElement.GetProperty("code").GetString());
        }
        else
        {
            Assert.NotEqual(privateCode, document.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Unexpected_owner_failure_is_code_less_503_without_position_disclosure()
    {
        var owner = new StubPositions(Positions()) { Failure = new InvalidOperationException("private_owner_detail") };
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private_owner_detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_owner_failure_is_code_less_and_discloses_no_position()
    {
        var product = new StubProductDisplays(ProductDisplays())
        {
            Failure = new ProductOperationException("private_product_detail", 503),
        };
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(Positions()), product);
        using var client = CreateClient(host);

        using var request = ProductDisplayRequest(InventoryStockPositionApiV1.PositionsPath);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private_product_detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
        Assert.Single(product.Queries);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("mismatch")]
    public async Task Invalid_Organization_batch_is_code_less_503_without_position_disclosure(string invalid)
    {
        var other = Guid.Parse("55555555-5555-4555-8555-555555555555");
        var answer = invalid switch
        {
            "missing" => Array.Empty<OrganizationLocationDisplayV1>(),
            "duplicate" => new[]
            {
                new OrganizationLocationDisplayV1(LocationId, "DEP-01", "Ana Depo"),
                new OrganizationLocationDisplayV1(LocationId, "DEP-01", "Ana Depo"),
            },
            _ => new[] { new OrganizationLocationDisplayV1(other, "DEP-02", "Başka Depo") },
        };
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(Positions()),
            locations: new StubLocationDisplays(answer));
        using var client = CreateClient(host);

        using var request = LocationDisplayRequest(InventoryStockPositionApiV1.PositionsPath);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.NotEqual(InventoryStockPositionCodesV1.Unavailable,
            document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Organization_owner_failure_is_code_less_503_without_owner_or_position_disclosure()
    {
        var locations = new StubLocationDisplays(LocationDisplays())
        {
            Failure = new InvalidOperationException("private_location_detail"),
        };
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(Positions()), locations: locations);
        using var client = CreateClient(host);

        using var request = LocationDisplayRequest(InventoryStockPositionApiV1.PositionsPath);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.NotEqual("private_location_detail", document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private_location_detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ErpPermissions.InventoryLabelsRead)]
    [InlineData(ErpPermissions.InventoryBalancesRead)]
    public async Task Inventory_permission_revoked_during_Product_read_refuses_without_old_positions(string permission)
    {
        var authority = new ControlledAuthority();
        var product = new StubProductDisplays(ProductDisplays()) { DuringRead = () => authority.MissingPermission = permission };
        using var host = CreateHost(authority, new StubPositions(Positions()), product);
        using var client = CreateClient(host);
        using var request = ProductDisplayRequest(InventoryStockPositionApiV1.PositionsPath);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("ROLL-1", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task Actor_or_company_changed_during_Product_read_refuses_without_old_positions(string changed)
    {
        var company = new MutableCompanyContext();
        var user = new MutableCurrentUser();
        var product = new StubProductDisplays(ProductDisplays())
        {
            DuringRead = () =>
            {
                if (changed == "actor") user.UserId = "other-actor";
                else company.Set(TestCompanyIds.CompanyB);
            },
        };
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(Positions()), product,
            company: company, user: user);
        using var client = CreateClient(host);
        using var request = ProductDisplayRequest(InventoryStockPositionApiV1.PositionsPath);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("ROLL-1", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("permission")]
    public async Task Authority_drift_during_Organization_read_is_code_less_403(string changed)
    {
        var authority = new ControlledAuthority();
        var company = new MutableCompanyContext();
        var user = new MutableCurrentUser();
        var locations = new StubLocationDisplays(LocationDisplays())
        {
            DuringRead = () =>
            {
                if (changed == "actor") user.UserId = "other-actor";
                else if (changed == "company") company.Set(TestCompanyIds.CompanyB);
                else authority.MissingPermission = ErpPermissions.InventoryBalancesRead;
            },
        };
        using var host = CreateHost(authority, new StubPositions(Positions()), locations: locations,
            company: company, user: user);
        using var client = CreateClient(host);

        using var request = LocationDisplayRequest(InventoryStockPositionApiV1.PositionsPath);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.NotEqual(InventoryStockPositionCodesV1.Forbidden,
            document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateHost(ControlledAuthority authority, StubPositions owner,
        StubProductDisplays? product = null, StubLocationDisplays? locations = null,
        MutableCompanyContext? company = null, MutableCurrentUser? user = null)
    {
        product ??= new StubProductDisplays(ProductDisplays());
        locations ??= new StubLocationDisplays(LocationDisplays());
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<IInventoryStockPositionsV1>();
            services.AddSingleton<IInventoryStockPositionsV1>(owner);
            services.RemoveAll<IProductStockDisplayReferencesV1>();
            services.AddSingleton<IProductStockDisplayReferencesV1>(product);
            services.RemoveAll<IOrganizationLocationDisplayReaderV1>();
            services.AddSingleton<IOrganizationLocationDisplayReaderV1>(locations);
            if (company is not null)
            {
                services.RemoveAll<ICompanyContext>();
                services.AddSingleton<ICompanyContext>(company);
            }
            if (user is not null)
            {
                services.RemoveAll<ICurrentUser>();
                services.AddSingleton<ICurrentUser>(user);
            }
        }));
    }

    private static HttpRequestMessage ProductDisplayRequest(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add(InventoryStockPositionApiV1.ProductDisplayContractHeader,
            InventoryStockPositionApiV1.ProductDisplayContractVersion);
        return request;
    }

    private static HttpRequestMessage LocationDisplayRequest(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add(InventoryStockPositionApiV1.ProductDisplayContractHeader,
            InventoryStockPositionDisplayHost.LocationDisplayContractVersion);
        return request;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static InventoryStockPositionsV1 Positions() => new([
        new InventoryStockPositionV1(
            TestCompanyIds.CompanyA, LabelId, "3", "ROLL-1", ProductId,
            "7", "Standard",
            new InventoryLotV1(LotId, "HEAT-1", "COIL-1"), "HEAT-1", "COIL-1", "Complete",
            "Company", TestCompanyIds.CompanyA, "Company", TestCompanyIds.CompanyA,
            LocationId, "10", "8", "2", "1", "7"),
    ], false);

    private static ProductStockDisplayBatchV1 ProductDisplays() => new([
        new(ProductId, "7", new(ProductId, "7", "P-1", "Galvaniz bant"),
            new(new(Guid.NewGuid(), "coil", "#66/stock-display"),
                new(Guid.NewGuid(), "1", "coil-v1", "#66/stock-display"), 1.5m, 1250m), []),
    ]);

    private static IReadOnlyList<OrganizationLocationDisplayV1> LocationDisplays() =>
        [new(LocationId, "DEP-01", "Ana Depo")];

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubPositions(InventoryStockPositionsV1 answer) : IInventoryStockPositionsV1
    {
        public List<InventoryStockPositionQueryV1> Queries { get; } = [];
        public Exception? Failure { get; init; }

        public Task<InventoryStockPositionsV1> SearchAsync(
            InventoryStockPositionQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(answer);
        }
    }

    private sealed class StubProductDisplays(ProductStockDisplayBatchV1 answer)
        : IProductStockDisplayReferencesV1
    {
        public List<ProductStockDisplayBatchQueryV1> Queries { get; } = [];
        public Exception? Failure { get; init; }
        public Action? DuringRead { get; init; }

        public Task<ProductStockDisplayBatchV1> ReadAsync(ProductStockDisplayBatchQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            DuringRead?.Invoke();
            if (Failure is not null) throw Failure;
            return Task.FromResult(answer);
        }
    }

    private sealed class StubLocationDisplays(IReadOnlyList<OrganizationLocationDisplayV1> answer)
        : IOrganizationLocationDisplayReaderV1
    {
        public List<(Guid CompanyId, IReadOnlyCollection<Guid> LocationIds)> Queries { get; } = [];
        public Exception? Failure { get; init; }
        public Action? DuringRead { get; init; }

        public Task<IReadOnlyList<OrganizationLocationDisplayV1>> ReadAsync(
            Guid companyId,
            IReadOnlyCollection<Guid> locationIds,
            CancellationToken cancellationToken = default)
        {
            Queries.Add((companyId, locationIds));
            DuringRead?.Invoke();
            if (Failure is not null) throw Failure;
            return Task.FromResult(answer);
        }
    }

    private sealed class ControlledAuthority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public string? MissingPermission { get; set; }
        public List<string> CheckedPermissions { get; } = [];

        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            CheckedPermissions.Add(permission);
            return Task.FromResult(companyId == TestCompanyIds.CompanyA && permission != MissingPermission);
        }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            CheckedPermissions.Add(permission);
            var allowed = companyId == TestCompanyIds.CompanyA && permission != MissingPermission;
            return Task.FromResult(allowed
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

    private sealed class MutableCompanyContext : ICompanyContext
    {
        public bool IsResolved { get; private set; }
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) { CompanyId = companyId; IsResolved = true; }
    }

    private sealed class MutableCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = "group-bootstrap-admin";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>
            { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
