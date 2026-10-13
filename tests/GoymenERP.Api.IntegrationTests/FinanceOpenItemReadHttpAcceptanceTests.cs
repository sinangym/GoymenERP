using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class FinanceOpenItemReadHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid ItemId = Guid.Parse("14000000-0000-0000-0000-000000000001");
    private static readonly Guid CurrencyId = Guid.Parse("14000000-0000-0000-0000-000000000002");
    private static string DetailPath => FinanceOpenItemReadEndpoints.Path + $"/{ItemId:D}";

    [Theory]
    [InlineData(FinancePermissionNames.OpenItemRead, ErpPermissions.FinanceOpenItemsRead)]
    [InlineData(FinancePermissionNames.OpenItemAmountRead, ErpPermissions.FinanceOpenItemsAmountRead)]
    public void Owner_actions_are_separate_central_company_scoped_read_names(string owner, string central)
    {
        Assert.Equal(owner, central);
        Assert.Single(ErpPermissions.All, value => value == owner);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(owner));
        Assert.False(ErpPermissions.IsCritical(owner));
        Assert.NotEqual(ErpPermissions.FinanceOpenItemsRead, ErpPermissions.FinanceOpenItemsAmountRead);
    }

    [Fact]
    public async Task Registration_grants_nothing_and_default_actor_cannot_reach_the_owner()
    {
        var service = new CapturingService();
        using var host = Host(service, allowed: false);
        using var client = Client(host);
        using var response = await client.GetAsync(FinanceOpenItemReadEndpoints.Path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, service.Calls);
        using var anonymous = host.CreateClient();
        using var unauthenticated = await anonymous.GetAsync(DetailPath);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Amount_authority_alone_never_grants_list_or_detail_read()
    {
        var service = new CapturingService { Resource = Item(visible: true) };
        using var host = Host(service, permission: ErpPermissions.FinanceOpenItemsAmountRead);
        using var client = Client(host);
        using var list = await client.GetAsync(FinanceOpenItemReadEndpoints.Path);
        using var detail = await client.GetAsync(DetailPath);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Metadata_only_response_is_private_and_contains_no_monetary_value_or_derived_total()
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);
        using var response = await client.GetAsync(FinanceOpenItemReadEndpoints.Path + "?cursor=opaque-cursor");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PrivateNoStore(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(TestCompanyIds.CompanyA, body.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal(["companyId", "items", "nextCursor"], body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("status").ValueKind);
        foreach (var property in new[] { "initialAmount", "outstandingAmount" })
        {
            var amount = item.GetProperty(property);
            Assert.Equal("Redacted", amount.GetProperty("disclosure").GetString());
            Assert.Equal(JsonValueKind.Null, amount.GetProperty("money").ValueKind);
        }
        Assert.DoesNotContain("123456.78", item.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("total", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("balance", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("opaque-cursor", service.Cursor);
        // Bootstrap subjects are resolved to the existing Identity user ID before the Finance owner is called.
        Assert.Equal("20000000-0000-0000-0000-000000000003", service.Context?.ActorId);
        Assert.Equal(TestCompanyIds.CompanyA, service.Context?.CompanyId);
    }

    [Fact]
    public async Task Owner_disclosed_money_and_source_versions_round_trip_without_transformation()
    {
        var service = new CapturingService { Resource = Item(visible: true) };
        using var host = Host(service);
        using var client = Client(host);
        using var response = await client.GetAsync(DetailPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PrivateNoStore(response);
        var result = await response.Content.ReadFromJsonAsync<FinanceOpenItemViewV1>(FinanceJson.CreateOptions());
        Assert.Equal(service.Resource, result);
        Assert.Equal(ItemId, service.ItemId);
        Assert.Equal("9007199254740993", result?.SourceVersion);
        Assert.Equal("123456.78", result?.InitialAmount.Money?.Amount);
    }

    [Theory]
    [InlineData(ReadOutcome.Denied, 403, "forbidden")]
    [InlineData(ReadOutcome.Unavailable, 503, "service_unavailable")]
    [InlineData(ReadOutcome.NotFound, 404, null)]
    public async Task Detail_failure_exposes_only_the_public_error_boundary(ReadOutcome outcome, int status, string? code)
    {
        var service = new CapturingService { Outcome = outcome };
        using var host = Host(service);
        using var client = Client(host);
        using var response = await client.GetAsync(DetailPath);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        PrivateNoStore(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (code is null) Assert.False(body.RootElement.TryGetProperty("code", out _));
        else Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(ItemId.ToString("D"), body.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("?unknown=1")]
    [InlineData("?cursor=a&cursor=b")]
    [InlineData("?cursor=")]
    [InlineData("?cursor=%20")]
    [InlineData("?Cursor=a")]
    [InlineData("?amount=1")]
    public async Task List_query_allowlist_rejects_invalid_queries_before_owner_read(string query)
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);
        using var response = await client.GetAsync(FinanceOpenItemReadEndpoints.Path + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        PrivateNoStore(response);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Detail_rejects_queries_and_missing_company_context_and_has_no_write_route()
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);
        using var query = await client.GetAsync(DetailPath + "?cursor=a");
        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        using var withoutCompany = Client(host, includeCompany: false);
        using var missing = await withoutCompany.GetAsync(DetailPath);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var post = await client.PostAsJsonAsync(FinanceOpenItemReadEndpoints.Path, new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    private WebApplicationFactory<Program> Host(CapturingService service, bool allowed = true,
        string permission = ErpPermissions.FinanceOpenItemsRead) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFinanceOpenItemReadServiceV1>();
            services.AddSingleton<IFinanceOpenItemReadServiceV1>(service);
            if (allowed)
            {
                var authority = new ReadAuthority(permission);
                services.RemoveAll<IErpAuthorizationService>();
                services.RemoveAll<IErpAuthorityRevalidationService>();
                services.AddSingleton<IErpAuthorizationService>(authority);
                services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            }
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host, bool includeCompany = true)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        if (includeCompany) client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static void PrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private static FinanceOpenItemViewV1 Item(bool visible = false) => new(
        ItemId, TestCompanyIds.CompanyA, "approved-file", Guid.NewGuid(), Guid.NewGuid(),
        "9007199254740993", Guid.NewGuid(), Guid.NewGuid(), CurrencyId, "1", null,
        visible ? FinanceOpenItemStatusV1.Open : null, new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero),
        new(visible ? AmountDisclosure.Visible : AmountDisclosure.Redacted, visible ? new("123456.78", CurrencyId) : null),
        new(visible ? AmountDisclosure.Visible : AmountDisclosure.Redacted, visible ? new("99999.01", CurrencyId) : null));

    private sealed class CapturingService : IFinanceOpenItemReadServiceV1
    {
        public FinanceOpenItemViewV1 Resource { get; init; } = Item();
        public ReadOutcome Outcome { get; init; } = ReadOutcome.Found;
        public int Calls { get; private set; }
        public FinanceContext? Context { get; private set; }
        public string? Cursor { get; private set; }
        public Guid? ItemId { get; private set; }
        public Task<ReadResult<FinanceOpenItemPageV1>> ListAuthorizedAsync(FinanceContext context, SourceListRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            Cursor = request.Cursor;
            return Task.FromResult(new ReadResult<FinanceOpenItemPageV1>(Outcome,
                Outcome == ReadOutcome.Found ? new(context.CompanyId, [Resource], null) : null));
        }
        public Task<ReadResult<FinanceOpenItemViewV1>> DetailAuthorizedAsync(FinanceContext context, Guid openItemId, CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            ItemId = openItemId;
            return Task.FromResult(new ReadResult<FinanceOpenItemViewV1>(Outcome, Outcome == ReadOutcome.Found ? Resource : null));
        }
    }

    private sealed class ReadAuthority(string allowedPermission) : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == TestCompanyIds.CompanyA && permission == allowedPermission);
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("fi04-http-only", new HashSet<Guid> { TestCompanyIds.CompanyA },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId), StringComparer.Ordinal)));
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId));
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId));
        private PermissionScope Scope(string permission, Guid companyId) =>
            new(companyId == TestCompanyIds.CompanyA && permission == allowedPermission, true,
                new HashSet<string>(StringComparer.Ordinal) { "FI04-HTTP" },
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    }
}
