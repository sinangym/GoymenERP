using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Sales.Application.Contracts;
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
        Assert.True(authority.Calls >= 2);
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
    public async Task Consumer_revocation_after_owner_read_discards_the_response()
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(new([
            new(ProductId, "7", "PRIVATE-CODE", "PRIVATE-NAME", [new(UnitId, "kg", ProfileId, "3")])
        ], false)) { During = () => authority.Granted = false };
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(SalesProductCandidateEndpoints.Path + "?effectiveOn=2026-09-22");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.DoesNotContain("PRIVATE-", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validation", 400, "validation_failed")]
    [InlineData("unavailable", 503, "service_unavailable")]
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

    private WebApplicationFactory<Program> CreateHost(ControlledAuthority authority, StubCandidates owner) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IProductSalesCandidatesV1>();
            services.AddSingleton<IProductSalesCandidatesV1>(owner);
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
                _ => Task.FromResult(result)
            };
        }
    }

    private sealed class ControlledAuthority : IErpAuthorizationService
    {
        public bool Granted { get; set; } = true;
        public int Calls { get; private set; }

        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Granted && permission == SalesPermissionManifestV1.DraftsCreate
                && companyId == TestCompanyIds.CompanyA);
        }

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
