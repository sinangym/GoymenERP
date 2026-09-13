using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SalesReferenceHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid PartyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid CurrencyId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ProductId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid UnitId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private const string AccountsQuery = "?q=CARI&pageSize=50&cursor=owner-cursor";

    [Fact]
    public async Task Default_runtime_denies_without_an_accepted_sales_permission_binding()
    {
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        using var response = await client.GetAsync(SalesEndpoints.AccountsPath + "?pageSize=50");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
    }

    [Fact]
    public async Task Accounts_success_is_lossless_private_and_uses_server_session_binding()
    {
        var service = new StubService
        {
            Accounts = new(200, new(TestCompanyIds.CompanyA,
                [new(PartyId, AccountId, "CARI - Cari", "7", "9007199254740993")],
                "next-owner-cursor"), null)
        };
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.AccountsPath + AccountsQuery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("9007199254740993", document.RootElement.GetProperty("accounts")[0]
            .GetProperty("accountVersion").GetString());
        Assert.Equal("next-owner-cursor", document.RootElement.GetProperty("nextCursor").GetString());
        Assert.Equal("trusted-session", Assert.Single(service.Sessions));
    }

    [Theory]
    [InlineData("?q=a&pageSize=50&pageSize=51", "validation_failed")]
    [InlineData("?pageSize=50&companyId=10000000-0000-0000-0000-000000000001", "validation_failed")]
    public async Task Accounts_reject_repeated_or_unknown_query_before_owner_service(
        string query, string expectedCode)
    {
        var service = new StubService();
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.AccountsPath + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length != 0)
        {
            using var document = JsonDocument.Parse(body);
            Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        }
        Assert.Empty(service.Sessions);
    }

    [Fact]
    public async Task Missing_server_session_fails_closed_before_owner_service()
    {
        var service = new StubService();
        using var host = CreateHost(service, new StubSessionBinding(null));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.AccountsPath + "?pageSize=50");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(service.Sessions);
    }

    [Theory]
    [InlineData(400, "validation_failed", "validation_failed")]
    [InlineData(403, "private-provider-code", "forbidden")]
    [InlineData(422, "unsupported_currency", "unsupported_currency")]
    [InlineData(503, "settings_provider_unavailable", "service_unavailable")]
    public async Task Currency_status_boundary_exposes_only_accepted_public_codes(
        int status, string sourceCode, string expectedPublicCode)
    {
        var service = new StubService
        {
            Currency = new(status, null, sourceCode)
        };
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.CurrencyPath
            + $"?currencyId={CurrencyId:D}&effectiveOn=2026-09-13");
        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedPublicCode, document.RootElement.GetProperty("code").GetString());
        if (!string.Equals(sourceCode, expectedPublicCode, StringComparison.Ordinal))
            Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_rejects_non_iso_business_date_without_calling_service()
    {
        var service = new StubService();
        using var host = CreateHost(service, new StubSessionBinding("trusted-session"));
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(SalesEndpoints.ProductPath
            + $"?productId={ProductId:D}&expectedProductVersion=7&orderUnitId={UnitId:D}&effectiveOn=09%2F13%2F2026");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, service.ProductCalls);
    }

    [Fact]
    public async Task Counterparty_adapter_requests_active_roles0_code_order_without_count()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(TestCompanyIds.CompanyA);
        var owner = new StubCounterpartyReadService(CounterpartyPageExecution.Succeeded(new(
            [new(PartyId, "CARI", PartyKind.LegalEntity, "Cari", null, true, 7,
                [new(AccountId, TestCompanyIds.CompanyA, PartyRole.None, true, 9007199254740993)])],
            "owner-next", true, null, null)));
        var adapter = new CounterpartySalesAccountSelectionSourceV1(companyContext,
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA), owner);

        var result = await adapter.GetPageAsync(new(TestCompanyIds.CompanyA, "sales-reader",
            "trusted-session", "CARI", 25, "owner-cursor"));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("9007199254740993", Assert.Single(Assert.Single(result.Value!.Items)
            .CompanyAccounts).Version);
        var request = Assert.IsType<CounterpartyReadRequest>(owner.Request);
        Assert.Equal(PartyRole.None, request.Query.Roles);
        Assert.Equal(CounterpartyRecordState.Active, request.Query.PartyState);
        Assert.Equal(CounterpartyRecordState.Active, request.Query.AccountState);
        Assert.Equal(CounterpartySort.Code, request.Query.Sort);
        Assert.Equal(CounterpartySortDirection.Ascending, request.Query.Direction);
        Assert.False(request.Query.IncludeCount);
        Assert.Equal("trusted-session", request.SessionBinding);
    }

    [Fact]
    public async Task Counterparty_adapter_denies_foreign_company_before_owner_read()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(TestCompanyIds.CompanyA);
        var owner = new StubCounterpartyReadService(CounterpartyPageExecution.ServiceUnavailable());
        var adapter = new CounterpartySalesAccountSelectionSourceV1(companyContext,
            new StubCurrentUser("sales-reader", TestCompanyIds.CompanyA), owner);
        var result = await adapter.GetPageAsync(new(TestCompanyIds.CompanyB, "sales-reader",
            "trusted-session", null, 50, null));
        Assert.Equal(403, result.StatusCode);
        Assert.Null(owner.Request);
    }

    private WebApplicationFactory<Program> CreateHost(ISalesReferenceReadService service,
        IActorSessionBindingResolver resolver) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesReferenceReadService>();
            services.AddSingleton(service);
            services.RemoveAll<IActorSessionBindingResolver>();
            services.AddSingleton(resolver);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host, bool includeCompany)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        if (includeCompany)
            client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubSessionBinding(string? binding) : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(binding);
    }

    private sealed class StubService : ISalesReferenceReadService
    {
        public SalesReferenceReadResult<SalesAccountOptionsV1> Accounts { get; set; } = new(403, null, null);
        public SalesReferenceReadResult<SalesCurrencyReferenceV1> Currency { get; set; } = new(403, null, null);
        public SalesReferenceReadResult<SalesProductReferenceV1> Product { get; set; } = new(403, null, null);
        public List<string> Sessions { get; } = [];
        public int ProductCalls { get; private set; }
        public Task<SalesReferenceReadResult<SalesAccountOptionsV1>> GetAccountsAsync(
            SalesAccountQueryV1 query, string sessionBinding, CancellationToken cancellationToken = default)
        { Sessions.Add(sessionBinding); return Task.FromResult(Accounts); }
        public Task<SalesReferenceReadResult<SalesCurrencyReferenceV1>> GetCurrencyAsync(
            SalesCurrencyQueryV1 query, CancellationToken cancellationToken = default) => Task.FromResult(Currency);
        public Task<SalesReferenceReadResult<SalesProductReferenceV1>> GetProductAsync(
            SalesProductQueryV1 query, CancellationToken cancellationToken = default)
        { ProductCalls++; return Task.FromResult(Product); }
    }

    private sealed class StubCurrentUser(string userId, params Guid[] companies) : ICurrentUser
    {
        private readonly HashSet<Guid> _companies = [.. companies];
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds => _companies;
        public bool CanAccessCompany(Guid companyId) => _companies.Contains(companyId);
    }

    private sealed class StubCounterpartyReadService(CounterpartyPageExecution execution)
        : ICounterpartyReadService
    {
        public CounterpartyReadRequest? Request { get; private set; }
        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request,
            CancellationToken cancellationToken = default)
        { Request = request; return Task.FromResult(execution); }
        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
