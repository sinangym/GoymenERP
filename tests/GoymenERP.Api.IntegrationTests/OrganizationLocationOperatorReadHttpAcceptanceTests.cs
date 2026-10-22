using System.Net;
using System.Text.Json;
using GoymenERP.Api.OrganizationRuntime;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OrganizationLocationOperatorReadHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid Location = Guid.Parse("75000000-0000-4000-8000-000000000001");
    private static readonly Guid Party = Guid.Parse("74000000-0000-4000-8000-000000000001");
    private static string DisplayPath => $"/api/v1/organization/locations/{Location:D}/operator";
    private static string CandidatePath => $"/api/v1/organization/locations/{Location:D}/operator-candidates";

    [Fact]
    public async Task Display_and_candidates_publish_the_frozen_named_shape_with_private_cache_policy()
    {
        var host = new StubHost
        {
            Display = new(
                OrganizationLocationOperatorReadOutcomeV1.Success,
                new(Location, new(Party, "CAR-1", "Tedarikçi A.Ş.", "Tedarikçi", true))),
            Candidates = new(
                OrganizationLocationOperatorReadOutcomeV1.Success,
                new([new(Party, "CAR-1", "Tedarikçi A.Ş.", "Tedarikçi")], "opaque-next", true)),
        };
        using var application = CreateHost(host);
        using var client = CreateClient(application);

        using var display = await client.GetAsync(DisplayPath);
        using var candidates = await client.GetAsync(
            CandidatePath + "?q=%20TEDARİKÇİ%20&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, display.StatusCode);
        Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
        AssertPrivateNoStore(display);
        AssertPrivateNoStore(candidates);
        using var displayBody = JsonDocument.Parse(await display.Content.ReadAsStringAsync());
        Assert.Equal(Location, displayBody.RootElement.GetProperty("locationId").GetGuid());
        var operatorRow = displayBody.RootElement.GetProperty("operator");
        Assert.Equal(Party, operatorRow.GetProperty("partyId").GetGuid());
        Assert.Equal("CAR-1", operatorRow.GetProperty("code").GetString());
        Assert.Equal("Tedarikçi A.Ş.", operatorRow.GetProperty("legalName").GetString());
        Assert.Equal("Tedarikçi", operatorRow.GetProperty("tradeName").GetString());
        Assert.True(operatorRow.GetProperty("isCurrentlyEligible").GetBoolean());
        using var candidateBody = JsonDocument.Parse(await candidates.Content.ReadAsStringAsync());
        Assert.Equal("opaque-next", candidateBody.RootElement.GetProperty("nextCursor").GetString());
        Assert.True(candidateBody.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(Party, Assert.Single(candidateBody.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("partyId").GetGuid());
        Assert.Equal((Location, "TEDARİKÇİ", 1, (string?)null, "server-session"),
            Assert.Single(host.CandidateCalls));
    }

    [Theory]
    [InlineData("display", (int)OrganizationLocationOperatorReadOutcomeV1.NotFound, 404, null)]
    [InlineData("display", (int)OrganizationLocationOperatorReadOutcomeV1.Unavailable, 503,
        "service_unavailable")]
    [InlineData("candidates", (int)OrganizationLocationOperatorReadOutcomeV1.NotFound, 404, null)]
    [InlineData("candidates", (int)OrganizationLocationOperatorReadOutcomeV1.InvalidCursor, 400, "invalid_cursor")]
    [InlineData("candidates", (int)OrganizationLocationOperatorReadOutcomeV1.Unavailable, 503,
        "service_unavailable")]
    public async Task Closed_host_outcomes_have_the_frozen_HTTP_mapping(
        string route,
        int outcome,
        int status,
        string? code)
    {
        var host = new StubHost
        {
            Display = new((OrganizationLocationOperatorReadOutcomeV1)outcome, null),
            Candidates = new((OrganizationLocationOperatorReadOutcomeV1)outcome, null),
        };
        using var application = CreateHost(host);
        using var client = CreateClient(application);

        using var response = await client.GetAsync(route == "display" ? DisplayPath : CandidatePath);

        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (code is null)
        {
            Assert.False(body.RootElement.TryGetProperty("code", out _));
        }
        else
        {
            Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        }
    }

    [Theory]
    [InlineData("?unknown=x", "validation_failed")]
    [InlineData("?q=a&q=b", "validation_failed")]
    [InlineData("?pageSize=26", "validation_failed")]
    [InlineData("?pageSize=not-an-integer", "validation_failed")]
    [InlineData("?cursor=%20", "invalid_cursor")]
    [InlineData("?cursor=a&cursor=b", "invalid_cursor")]
    public async Task Invalid_candidate_query_is_stable_400_before_the_host(string query, string code)
    {
        var host = new StubHost();
        using var application = CreateHost(host);
        using var client = CreateClient(application);

        using var response = await client.GetAsync(CandidatePath + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertPrivateNoStore(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.Empty(host.CandidateCalls);
    }

    [Fact]
    public async Task Missing_server_session_is_503_before_the_host()
    {
        var host = new StubHost();
        using var application = CreateHost(host, sessionBinding: null);
        using var client = CreateClient(application);

        using var response = await client.GetAsync(CandidatePath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("service_unavailable",
            body.RootElement.GetProperty("code").GetString());
        Assert.Empty(host.CandidateCalls);
    }

    private WebApplicationFactory<Program> CreateHost(StubHost host, string? sessionBinding = "server-session") =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOrganizationLocationOperatorReadHostV1>();
            services.AddSingleton<IOrganizationLocationOperatorReadHostV1>(host);
            services.RemoveAll<IActorSessionBindingResolver>();
            services.AddSingleton<IActorSessionBindingResolver>(new StubSession(sessionBinding));
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(new ControlledAuthority());
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> application)
    {
        var client = application.CreateClient();
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

    private sealed class StubSession(string? value) : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(value);
    }

    private sealed class ControlledAuthority : IErpAuthorizationService
    {
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId,
            IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default)
        {
            var allowedCompanyIds = companyId == TestCompanyIds.CompanyA
                ? new HashSet<Guid> { companyId }
                : [];
            return Task.FromResult(new AuthorizationSnapshot(
                "controlled-location-operator-http",
                allowedCompanyIds,
                permissions.ToDictionary(
                    permission => permission,
                    permission => Scope(permission, companyId))));
        }

        public Task<bool> HasPermissionAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId).IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId));

        private static PermissionScope Scope(string permission, Guid companyId)
        {
            if (companyId != TestCompanyIds.CompanyA
                || permission != ErpPermissions.OrganizationLocationsRead
                && permission != OrganizationPermissionManifestV1.LocationsWrite)
            {
                return PermissionScope.Denied;
            }

            return new PermissionScope(
                true,
                true,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>());
        }
    }

    private sealed class StubHost : IOrganizationLocationOperatorReadHostV1
    {
        public OrganizationLocationOperatorReadResultV1<OrganizationLocationOperatorContextV1> Display { get; init; } =
            new(OrganizationLocationOperatorReadOutcomeV1.Unavailable, null);
        public OrganizationLocationOperatorReadResultV1<OrganizationLocationOperatorCandidatePageV1> Candidates
            { get; init; } = new(OrganizationLocationOperatorReadOutcomeV1.Unavailable, null);
        public List<(Guid LocationId, string? Q, int PageSize, string? Cursor, string SessionBinding)>
            CandidateCalls { get; } = [];

        public Task<OrganizationLocationOperatorReadResultV1<OrganizationLocationOperatorContextV1>>
            ReadDisplayAsync(Guid locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Display);

        public Task<OrganizationLocationOperatorReadResultV1<OrganizationLocationOperatorCandidatePageV1>>
            SearchCandidatesAsync(Guid locationId, OrganizationLocationOperatorCandidateQueryV1 query,
                string sessionBinding, CancellationToken cancellationToken = default)
        {
            CandidateCalls.Add((locationId, query.Q, query.PageSize, query.Cursor, sessionBinding));
            return Task.FromResult(Candidates);
        }
    }
}
