using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryMovementTimelineHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid MovementId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SourceId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid LabelId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid LocationId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid ProductId = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public async Task Authorized_company_read_returns_exact_page_and_private_no_store()
    {
        var authority = new ControlledAuthority();
        var owner = new StubTimeline(Page());
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(
            InventoryMovementTimelineApiV1.Path + "?limit=12&cursor=opaque");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(new InventoryMovementTimelineQueryV1(12, "opaque"), Assert.Single(owner.Queries));
        Assert.Contains(ErpPermissions.InventoryLabelsRead, authority.CheckedPermissions);
        Assert.Contains(ErpPermissions.InventoryBalancesRead, authority.CheckedPermissions);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("next", document.RootElement.GetProperty("nextCursor").GetString());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(MovementId, item.GetProperty("movementId").GetGuid());
        Assert.Equal("Receipt", item.GetProperty("kind").GetString());
        var line = Assert.Single(item.GetProperty("lines").EnumerateArray());
        Assert.Equal(LabelId, line.GetProperty("labelId").GetGuid());
        Assert.Equal("ROLL-1", line.GetProperty("labelCode").GetString());
        Assert.Equal("8", line.GetProperty("quantityKg").GetString());
    }

    [Theory]
    [InlineData("?unknown=x")]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?limit=abc")]
    [InlineData("?limit=1&limit=2")]
    [InlineData("?cursor=")]
    [InlineData("?cursor=a&cursor=b")]
    public async Task Invalid_query_is_coded_400_before_owner_read(string query)
    {
        var owner = new StubTimeline(Page());
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryMovementTimelineApiV1.Path + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(owner.Queries);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(InventoryMovementTimelineCodesV1.InvalidQuery,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_company_context_is_coded_400_and_never_reaches_owner()
    {
        var owner = new StubTimeline(Page());
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);

        using var response = await client.GetAsync(InventoryMovementTimelineApiV1.Path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(owner.Queries);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("company_context_invalid", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unauthenticated_request_is_private_no_store_and_never_reaches_owner()
    {
        var owner = new StubTimeline(Page());
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));

        using var response = await client.GetAsync(InventoryMovementTimelineApiV1.Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(owner.Queries);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(InventoryMovementTimelineCodesV1.Unauthenticated, body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("authentication_required", document.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(ErpPermissions.InventoryLabelsRead)]
    [InlineData(ErpPermissions.InventoryBalancesRead)]
    public async Task Either_missing_read_permission_refuses_before_owner_read(string missing)
    {
        var owner = new StubTimeline(Page());
        using var host = CreateHost(new ControlledAuthority { MissingPermission = missing }, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryMovementTimelineApiV1.Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(owner.Queries);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(InventoryMovementTimelineCodesV1.Forbidden, body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("forbidden", document.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(400, InventoryMovementTimelineCodesV1.InvalidQuery,
        InventoryMovementTimelineCodesV1.InvalidQuery)]
    [InlineData(503, InventoryMovementTimelineCodesV1.Unavailable, "service_unavailable")]
    public async Task Owner_public_refusals_preserve_the_exact_coded_contract(
        int status,
        string privateCode,
        string publicCode)
    {
        var owner = new StubTimeline(Page())
        {
            Failure = new InventoryMovementTimelineException(status, privateCode),
        };
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryMovementTimelineApiV1.Path);

        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        if (!string.Equals(privateCode, publicCode, StringComparison.Ordinal))
        {
            Assert.DoesNotContain(privateCode, body, StringComparison.Ordinal);
        }
        using var document = JsonDocument.Parse(body);
        Assert.Equal(publicCode, document.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(401, InventoryMovementTimelineCodesV1.Unauthenticated, "authentication_required")]
    [InlineData(403, InventoryMovementTimelineCodesV1.Forbidden, "forbidden")]
    public async Task Owner_authority_refusals_use_only_platform_codes(
        int status,
        string privateCode,
        string publicCode)
    {
        var owner = new StubTimeline(Page())
        {
            Failure = new InventoryMovementTimelineException(status, privateCode),
        };
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryMovementTimelineApiV1.Path);

        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(privateCode, body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(publicCode, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unexpected_owner_failure_uses_platform_503_without_timeline_disclosure()
    {
        var owner = new StubTimeline(Page()) { Failure = new InvalidOperationException("private_owner_detail") };
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryMovementTimelineApiV1.Path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private_owner_detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain(InventoryMovementTimelineCodesV1.Unavailable, body, StringComparison.Ordinal);
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("service_unavailable", document.RootElement.GetProperty("code").GetString());
    }

    private WebApplicationFactory<Program> CreateHost(ControlledAuthority authority, StubTimeline owner) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<IInventoryMovementTimelineV1>();
            services.AddSingleton<IInventoryMovementTimelineV1>(owner);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static InventoryMovementTimelinePageV1 Page() => new([
        new InventoryMovementV1(MovementId, "Receipt", DateTimeOffset.Parse("2026-10-07T12:00:00Z"),
            "PurchaseReceipt", SourceId, null, "Whole", [
                new InventoryMovementLineV1(1, "In", LabelId, "ROLL-1", "Company", TestCompanyIds.CompanyA,
                    "Company", TestCompanyIds.CompanyA, LocationId, ProductId, "Free", "8"),
            ]),
    ], "next");

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubTimeline(InventoryMovementTimelinePageV1 answer) : IInventoryMovementTimelineV1
    {
        public List<InventoryMovementTimelineQueryV1> Queries { get; } = [];
        public Exception? Failure { get; init; }

        public Task<InventoryMovementTimelinePageV1> SearchAsync(
            InventoryMovementTimelineQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Failure is null
                ? Task.FromResult(answer)
                : Task.FromException<InventoryMovementTimelinePageV1>(Failure);
        }
    }

    private sealed class ControlledAuthority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public string? MissingPermission { get; init; }
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
            var granted = companyId == TestCompanyIds.CompanyA && permission != MissingPermission;
            return Task.FromResult(granted
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
