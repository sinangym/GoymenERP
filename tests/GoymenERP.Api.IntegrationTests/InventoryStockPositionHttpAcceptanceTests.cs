using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Errors;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
    private static readonly Guid SqlActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid SqlActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SqlSystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    internal static async Task RunGenuineSqlLocationDisplayJourneyAsync()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var databaseConnection = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
        var sqlFactory = new SqlServerRelationalApiFactory(baseConnection, services =>
        {
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(databaseConnection,
                new ProductPilotIssuerBinding(SqlActorId, SqlActorExternalId),
                Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()));
        }, databaseName: databaseName);

        try
        {
            using (var startup = sqlFactory.CreateClient())
            using (var health = await startup.GetAsync("/health/live"))
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            await GrantSqlJourneyAuthorityAsync(sqlFactory);
            using var actor = sqlFactory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);

            var references = new ProductReferences(
                ProductInitialTechnicalDefinitionV1.TemplateId,
                "1",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["thickness-mm"] = JsonSerializer.SerializeToElement(3m),
                    ["width-mm"] = JsonSerializer.SerializeToElement(248m),
                    ["steel-grade"] = JsonSerializer.SerializeToElement("S235JR"),
                    ["coating"] = JsonSerializer.SerializeToElement("uncoated"),
                },
                new ProductUnitReferences(Stock: SettingsUnitIdentitiesV1.Kilogram,
                    Sales: SettingsUnitIdentitiesV1.Kilogram));
            using var created = await SendSqlCommandAsync(actor, HttpMethod.Post, ProductApiV1.BasePath,
                new CreateProductDraftRequest("S235JR 3 mm × 248 mm kaplamasız sac",
                    ProductInitialCodeGroupV1.SacGroupId,
                    FamilyId: ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
                    References: references),
                "inventory-location-display-product-create");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var draft = (await created.Content.ReadFromJsonAsync<ProductMutationResponse>())!;

            using var activated = await SendSqlCommandAsync(actor, HttpMethod.Post,
                $"/api/v1/products/{draft.Card.Product.Id:D}/activation",
                new ActivateProductRequest(draft.Card.Product.Version),
                "inventory-location-display-product-activate");
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
            var active = (await activated.Content.ReadFromJsonAsync<ProductMutationResponse>())!;

            using var locationsResponse = await actor.GetAsync(InventoryReferenceEndpoints.LocationPath);
            Assert.Equal(HttpStatusCode.OK, locationsResponse.StatusCode);
            var locations = (await locationsResponse.Content.ReadFromJsonAsync<OrganizationLocationCandidatesV1>())!;
            Assert.NotEmpty(locations.Items);
            var location = locations.Items[0];

            var labelId = Guid.NewGuid();
            var opening = new OpeningCountRequestV1(Guid.NewGuid(), location.LocationId,
                new DateOnly(2026, 10, 7), "Disposable stock-position location composition",
                [new OpeningCountLineRequestV1(Guid.NewGuid(), labelId, "SAC-LOCATION-DISPLAY-1",
                    Guid.NewGuid(), null, null, active.Card.Product.Id, active.Card.Product.Version, "1000")]);
            using var opened = await SendSqlCommandAsync(actor, HttpMethod.Post,
                "/api/v1/inventory/opening-counts", opening,
                "inventory-location-display-opening-create");
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);

            using var freshActor = sqlFactory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            freshActor.DefaultRequestHeaders.Add(InventoryStockPositionApiV1.ProductDisplayContractHeader,
                InventoryStockPositionDisplayHost.LocationDisplayContractVersion);
            using var response = await freshActor.GetAsync(
                $"{InventoryStockPositionApiV1.PositionsPath}?productId={active.Card.Product.Id:D}" +
                $"&locationId={location.LocationId:D}&limit=25");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = (await response.Content.ReadFromJsonAsync<InventoryStockPositionLocationPageV2>())!;
            var position = Assert.Single(page.Items);
            Assert.Equal((labelId, location.LocationId, active.Card.Product.Id, active.Card.Product.Version),
                (position.LabelId, position.LocationId, position.ProductId, position.ProductVersion));
            var productIdentity = Assert.IsType<ProductStockDisplayIdentityV1>(position.ProductDisplay.Identity);
            var productTechnical = Assert.IsType<ProductStockNominalTechnicalV1>(position.ProductDisplay.Technical);
            Assert.Equal((active.Card.Product.Id, active.Card.Product.Version,
                    active.Card.Product.Code, active.Card.Product.Name),
                (productIdentity.ProductId, productIdentity.ProductVersion,
                    productIdentity.Code, productIdentity.Name));
            Assert.Equal((3m, 248m),
                (productTechnical.ThicknessMm, productTechnical.WidthMm));
            Assert.Equal(new OrganizationLocationDisplayV1(location.LocationId,
                    location.LocationCode, location.LocationName),
                position.LocationDisplay);
            Assert.Equal(position.LocationId, position.LocationDisplay.LocationId);
        }
        finally
        {
            await sqlFactory.DisposeAsync();
            await DropSqlDatabaseAsync(baseConnection, databaseName);
        }
    }

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
    public async Task Owner_display_3_0_composes_only_the_record_bound_counterparty_owner()
    {
        var partyId = Guid.Parse("73000000-0000-0000-0000-000000000001");
        var owners = new StubStockOwnerDisplays(new(
            InventoryStockOwnerDisplayBatchOutcomeV1.Found,
            [new(partyId, "CAR-993001", "Tarihsel Stok Maliki", "Malik Ticaret")]));
        using var host = CreateHost(
            new ControlledAuthority(),
            new StubPositions(CounterpartyPositions(partyId)),
            stockOwners: owners);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(OwnerDisplayRequest(InventoryStockPositionApiV1.PositionsPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(owners.Queries);
        Assert.Equal(TestCompanyIds.CompanyA, request.CompanyId);
        Assert.Equal(SqlActorId.ToString("D"), request.ActorId);
        Assert.Equal([partyId], request.PartyIds);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("P-1", item.GetProperty("productDisplay").GetProperty("identity").GetProperty("code").GetString());
        Assert.Equal("DEP-01", item.GetProperty("locationDisplay").GetProperty("locationCode").GetString());
        var owner = item.GetProperty("ownerDisplay");
        Assert.Equal(partyId, owner.GetProperty("partyId").GetGuid());
        Assert.Equal("CAR-993001", owner.GetProperty("code").GetString());
        Assert.Equal("Tarihsel Stok Maliki", owner.GetProperty("legalName").GetString());
    }

    [Fact]
    public async Task Owner_display_3_0_skips_Cari_for_empty_and_company_owned_pages()
    {
        var owners = new StubStockOwnerDisplays(new(InventoryStockOwnerDisplayBatchOutcomeV1.Found, []));
        using var emptyHost = CreateHost(new ControlledAuthority(), new StubPositions(new([], false)),
            stockOwners: owners);
        using var emptyClient = CreateClient(emptyHost);
        using var empty = await emptyClient.SendAsync(OwnerDisplayRequest(InventoryStockPositionApiV1.PositionsPath));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);

        using var companyHost = CreateHost(new ControlledAuthority(), new StubPositions(Positions()),
            stockOwners: owners);
        using var companyClient = CreateClient(companyHost);
        using var company = await companyClient.SendAsync(OwnerDisplayRequest(InventoryStockPositionApiV1.PositionsPath));
        Assert.Equal(HttpStatusCode.OK, company.StatusCode);
        using var document = JsonDocument.Parse(await company.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, Assert.Single(document.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("ownerDisplay").ValueKind);
        Assert.Empty(owners.Queries);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    public async Task Missing_or_malformed_owner_projection_fails_closed_without_stock_disclosure(string kind)
    {
        var partyId = Guid.Parse("73000000-0000-0000-0000-000000000002");
        var answer = kind == "missing"
            ? new InventoryStockOwnerDisplayBatchAnswerV1(InventoryStockOwnerDisplayBatchOutcomeV1.Found, [])
            : new InventoryStockOwnerDisplayBatchAnswerV1(InventoryStockOwnerDisplayBatchOutcomeV1.Found,
                [new(partyId, " ", "Malformed", null)]);
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(CounterpartyPositions(partyId)),
            stockOwners: new StubStockOwnerDisplays(answer));
        using var client = CreateClient(host);

        using var response = await client.SendAsync(OwnerDisplayRequest(InventoryStockPositionApiV1.PositionsPath));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
        Assert.DoesNotContain(partyId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authority_drift_during_owner_projection_is_code_less_403()
    {
        var partyId = Guid.Parse("73000000-0000-0000-0000-000000000003");
        var authority = new ControlledAuthority();
        var owners = new StubStockOwnerDisplays(new(InventoryStockOwnerDisplayBatchOutcomeV1.Found,
            [new(partyId, "CAR-993003", "Revoked Owner", null)]))
        {
            DuringRead = () => authority.MissingPermission = ErpPermissions.InventoryBalancesRead,
        };
        using var host = CreateHost(authority, new StubPositions(CounterpartyPositions(partyId)),
            stockOwners: owners);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(OwnerDisplayRequest(InventoryStockPositionApiV1.PositionsPath));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
        Assert.DoesNotContain(partyId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
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
        foreach (var values in new[] { new[] { "4.0" }, new[] { "1.0", "1.0" } })
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

    [Fact]
    public async Task Product_operational_selection_refusal_uses_the_safe_platform_403_and_discloses_no_position()
    {
        var product = new StubProductDisplays(ProductDisplays())
        {
            Failure = new ProductOperationException("product_access_denied", 403),
        };
        using var host = CreateHost(new ControlledAuthority(), new StubPositions(Positions()), product);
        using var client = CreateClient(host);

        using var request = ProductDisplayRequest(InventoryStockPositionApiV1.PositionsPath);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(ErpProblemCodes.Forbidden,
            document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("product_access_denied", body, StringComparison.Ordinal);
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
        MutableCompanyContext? company = null, MutableCurrentUser? user = null,
        StubStockOwnerDisplays? stockOwners = null)
    {
        product ??= new StubProductDisplays(ProductDisplays());
        locations ??= new StubLocationDisplays(LocationDisplays());
        stockOwners ??= new StubStockOwnerDisplays(new(InventoryStockOwnerDisplayBatchOutcomeV1.Found, []));
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
            services.RemoveAll<IInventoryStockOwnerDisplaySourceV1>();
            services.AddSingleton<IInventoryStockOwnerDisplaySourceV1>(stockOwners);
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

    private static HttpRequestMessage OwnerDisplayRequest(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add(InventoryStockPositionApiV1.ProductDisplayContractHeader,
            InventoryStockPositionApiV1.OwnerDisplayContractVersion);
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

    private static InventoryStockPositionsV1 CounterpartyPositions(Guid partyId) => new([
        new InventoryStockPositionV1(
            TestCompanyIds.CompanyA, LabelId, "3", "ROLL-1", ProductId,
            "7", "Standard",
            new InventoryLotV1(LotId, "HEAT-1", "COIL-1"), "HEAT-1", "COIL-1", "Complete",
            "Counterparty", partyId, "Company", TestCompanyIds.CompanyA,
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

    private sealed class StubStockOwnerDisplays(InventoryStockOwnerDisplayBatchAnswerV1 answer)
        : IInventoryStockOwnerDisplaySourceV1
    {
        public List<InventoryStockOwnerDisplayBatchRequestV1> Queries { get; } = [];
        public Action? DuringRead { get; init; }

        public Task<InventoryStockOwnerDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
            InventoryStockOwnerDisplayBatchRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(request);
            DuringRead?.Invoke();
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

    private static async Task GrantSqlJourneyAuthorityAsync(SqlServerRelationalApiFactory sqlFactory)
    {
        await using var scope = sqlFactory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(identity.Database.IsSqlServer());
        foreach (var operation in Enum.GetValues<ProductMasterOperation>())
            identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), SqlActorId,
                SqlSystemRoleId, operation, SqlActorId, SqlActorExternalId, Guid.NewGuid(),
                "Disposable stock-position display SQL fixture; not a production grant",
                DateTimeOffset.UtcNow.AddMinutes(-1), null));

        if (!await identity.Users.AnyAsync(user => user.Id == SqlActorId))
            identity.Users.Add(new UserAccount(SqlActorId, "Disposable stock-position display SQL actor", null));
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"INVENTORY-LOCATION-DISPLAY-{roleId:N}",
            "Disposable Product, Inventory and Organization SQL journey actor", true);
        role.AddPermission(ProductPermissionManifestV1.OperationalSelectionRead);
        role.AddPermission(InventoryPermissionManifestV1.OpeningStockRecord);
        role.AddPermission(ErpPermissions.InventoryLabelsRead);
        role.AddPermission(ErpPermissions.InventoryBalancesRead);
        var assignment = new RoleAssignment(Guid.NewGuid(), SqlActorId, roleId, TestCompanyIds.CompanyA,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, TestCompanyIds.CompanyA);
        identity.Roles.Add(role);
        identity.RoleAssignments.Add(assignment);
        await identity.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> SendSqlCommandAsync<T>(HttpClient client, HttpMethod method,
        string path, T body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task DropSqlDatabaseAsync(string baseConnection, string databaseName)
    {
        if (!Regex.IsMatch(databaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Refusing cleanup outside this test's generated database identity.");
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = "master",
        }.ConnectionString);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN " +
                           $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                           $"DROP DATABASE [{databaseName}]; END;";
        await drop.ExecuteNonQueryAsync();
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

public sealed class InventoryStockPositionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Genuine_SQL_stock_position_composes_the_same_Product_Inventory_and_Organization_location_record() =>
        InventoryStockPositionHttpAcceptanceTests.RunGenuineSqlLocationDisplayJourneyAsync();
}
