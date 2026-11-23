using System.Net;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Api.IntegrationTests;

// Query contract and real HTTP authorization composition only; no persistence/SQL acceptance claim.
public sealed class InventoryNegativeStockAttemptQueryContractTests
{
    private const string Product = "82200000-0000-4000-8000-000000000001";
    private const string Location = "82200000-0000-4000-8000-000000000002";

    [Fact]
    public void An_empty_query_has_no_identity_filter_and_requests_fifty_open_and_resolved_records()
    {
        Assert.True(Parse("", out var query));
        Assert.Null(query.ProductId);
        Assert.Null(query.LocationId);
        Assert.False(query.OpenOnly);
        Assert.Equal(50, query.Limit);
    }

    [Theory]
    [InlineData("true", 1, true)]
    [InlineData("false", 50, false)]
    [InlineData("true", 100, true)]
    public void Explicit_identity_filters_and_canonical_window_values_are_preserved(string openOnly, int limit, bool expectedOpen)
    {
        Assert.True(Parse($"?productId={Product}&locationId={Location}&openOnly={openOnly}&limit={limit}", out var query));
        Assert.Equal(Guid.Parse(Product), query.ProductId);
        Assert.Equal(Guid.Parse(Location), query.LocationId);
        Assert.Equal(expectedOpen, query.OpenOnly);
        Assert.Equal(limit, query.Limit);
    }

    [Theory]
    [InlineData("productId")]
    [InlineData("locationId")]
    public void Either_identity_filter_can_be_used_independently(string key)
    {
        Assert.True(Parse($"?{key}={Product}", out var query));
        Assert.Equal(key == "productId" ? Guid.Parse(Product) : (Guid?)null, query.ProductId);
        Assert.Equal(key == "locationId" ? Guid.Parse(Product) : (Guid?)null, query.LocationId);
    }

    [Theory]
    [InlineData("?companyId=82200000-0000-4000-8000-000000000001")]
    [InlineData("?actorId=synthetic-actor")]
    [InlineData("?resolved=true")]
    [InlineData("?offset=50")]
    [InlineData("?Limit=50")]
    [InlineData("?productId=")]
    [InlineData("?locationId=")]
    [InlineData("?productId=not-a-guid")]
    [InlineData("?locationId=not-a-guid")]
    [InlineData("?productId=00000000-0000-0000-0000-000000000000")]
    [InlineData("?locationId=00000000-0000-0000-0000-000000000000")]
    [InlineData("?productId=82200000000040008000000000000001")]
    [InlineData("?locationId=%7B82200000-0000-4000-8000-000000000002%7D")]
    [InlineData("?openOnly=")]
    [InlineData("?openOnly=True")]
    [InlineData("?openOnly=FALSE")]
    [InlineData("?openOnly=1")]
    [InlineData("?openOnly=%20true")]
    [InlineData("?limit=")]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?limit=-1")]
    [InlineData("?limit=%2B1")]
    [InlineData("?limit=01")]
    [InlineData("?limit=1.0")]
    [InlineData("?limit=1e1")]
    [InlineData("?limit=%2050")]
    [InlineData("?limit=2147483648")]
    public void Unknown_identity_shapes_and_noncanonical_query_values_are_refused(string rawQuery)
    {
        Assert.False(Parse(rawQuery, out _));
    }

    [Theory]
    [InlineData("productId", Product)]
    [InlineData("locationId", Location)]
    [InlineData("openOnly", "true")]
    [InlineData("limit", "50")]
    public void Duplicate_query_keys_are_refused_even_when_values_are_identical(string key, string value)
    {
        Assert.False(Parse($"?{key}={value}&{key}={value}", out _));
    }

    [Fact]
    public void A_present_key_with_no_values_is_not_treated_as_an_omitted_filter()
    {
        var values = new QueryCollection(new Dictionary<string, StringValues> { ["openOnly"] = StringValues.Empty });
        Assert.False(InventoryNegativeStockAttemptEndpoints.TryQuery(values, out _));
    }

    [Fact]
    public async Task The_real_route_requires_both_existing_read_permissions_company_context_and_uncacheable_authorization_failures()
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health/live");
        health.EnsureSuccessStatusCode();
        var route = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
                == InventoryNegativeStockAttemptApiV1.GetOperationId);

        Assert.Equal(InventoryNegativeStockAttemptApiV1.Path, route.RoutePattern.RawText);
        Assert.Equal(["GET"], route.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.NotNull(route.Metadata.GetMetadata<RequiresCompanyContextAttribute>());
        Assert.NotNull(route.Metadata.GetMetadata<PrivateNoStoreOnAuthorizationFailureMetadata>());
        var permissions = route.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).OfType<string>().ToHashSet();
        Assert.Contains(ErpPermissions.InventoryLabelsRead, permissions);
        Assert.Contains(ErpPermissions.InventoryBalancesRead, permissions);
        var responses = route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        foreach (var status in new[] { 200, 400, 401, 403, 503 })
            Assert.Contains(responses, response => response.StatusCode == status);
        Assert.Contains(responses, response => response.StatusCode == 200 && response.Type == typeof(InventoryNegativeStockAttemptPageV1));
    }

    [Fact]
    public async Task Anonymous_and_unprivileged_real_host_requests_are_refused_before_the_report_owner_and_are_not_cached()
    {
        var owner = new RefuseIfCalledOwner();
        await using var factory = new GoymenErpApiFactory();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IInventoryNegativeStockAttemptsV1>(owner))));
        using var anonymous = app.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync(InventoryNegativeStockAttemptApiV1.Path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        AssertPrivateNoStore(anonymousResponse);

        using var reader = app.CreateClient();
        reader.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyAViewer);
        reader.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        using var readerResponse = await reader.GetAsync(InventoryNegativeStockAttemptApiV1.Path);
        Assert.Equal(HttpStatusCode.Forbidden, readerResponse.StatusCode);
        AssertPrivateNoStore(readerResponse);
        Assert.Equal(0, owner.Calls);
    }

    private static bool Parse(string raw, out InventoryNegativeStockAttemptQueryV1 query) =>
        InventoryNegativeStockAttemptEndpoints.TryQuery(new QueryCollection(QueryHelpers.ParseQuery(raw)), out query);

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private == true);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.Contains(response.Headers.Pragma, value => value.Name == "no-cache");
    }

    private sealed class RefuseIfCalledOwner : IInventoryNegativeStockAttemptsV1
    {
        public int Calls { get; private set; }

        public Task<InventoryNegativeStockAttemptPageV1> SearchAsync(InventoryNegativeStockAttemptQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromException<InventoryNegativeStockAttemptPageV1>(
                new InventoryNegativeStockAttemptException(503, InventoryNegativeStockAttemptCodesV1.Unavailable));
        }
    }
}
