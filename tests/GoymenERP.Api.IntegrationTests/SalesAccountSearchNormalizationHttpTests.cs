using System.Net;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// #262 patch item 5 (issuecomment-5667129884): the Sales accounts search text reaches the
/// owner exactly as the Cari list's own search would, so the same record is found from the
/// order screen and from the Cari screen. Reference: <c>CounterpartyPageQueryParser</c> —
/// trim, Unicode FormC, empty or whitespace-only means no search, 200 Unicode scalar values.
/// </summary>
public sealed class SalesAccountSearchNormalizationHttpTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    public static TheoryData<string, string?> Normalized => new()
    {
        // Surrounding whitespace is not part of the search.
        { "ACME%20", "ACME" },
        { "%20%20ACME%09", "ACME" },
        // "s" + U+0327 COMBINING CEDILLA composes to "ş" (U+015F), as the Cari list stores and matches it.
        { "s%CC%A7ahin", "şahin" },
        // An empty or whitespace-only search is no search — not a search for "".
        { "", null },
        { "%20%20%20", null },
    };

    [Theory]
    [MemberData(nameof(Normalized))]
    public async Task Search_text_reaches_the_owner_normalized(string rawQuery, string? expected)
    {
        var service = new RecordingService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync($"{SalesEndpoints.AccountsPath}?q={rawQuery}&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.Single(service.Queries);
        Assert.Equal(expected, query.Q);
        Assert.Equal(50, query.PageSize);
        Assert.Null(query.Cursor);
    }

    [Fact]
    public async Task No_search_parameter_stays_no_search()
    {
        var service = new RecordingService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync($"{SalesEndpoints.AccountsPath}?pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single(service.Queries).Q);
    }

    [Fact]
    public async Task Limit_counts_unicode_scalar_values_not_utf16_units()
    {
        // 200 astral characters are 400 UTF-16 units: inside the limit, as on the Cari list.
        var astral = string.Concat(Enumerable.Repeat("\U0001F600", 200));
        var service = new RecordingService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(
            $"{SalesEndpoints.AccountsPath}?q={Uri.EscapeDataString(astral)}&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(astral, Assert.Single(service.Queries).Q);
    }

    [Fact]
    public async Task Limit_applies_after_trimming()
    {
        var service = new RecordingService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var twoHundred = new string('a', 200);

        using var response = await client.GetAsync(
            $"{SalesEndpoints.AccountsPath}?q={Uri.EscapeDataString($"  {twoHundred}  ")}&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(twoHundred, Assert.Single(service.Queries).Q);
    }

    [Fact]
    public async Task Over_the_limit_is_refused_before_the_owner_is_asked()
    {
        var service = new RecordingService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(
            $"{SalesEndpoints.AccountsPath}?q={new string('a', 201)}&pageSize=50");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Queries);
    }

    private WebApplicationFactory<Program> CreateHost(ISalesReferenceReadService service) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesReferenceReadService>();
            services.AddSingleton(service);
            services.RemoveAll<IActorSessionBindingResolver>();
            services.AddSingleton<IActorSessionBindingResolver>(new FixedSessionBinding("trusted-session"));
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private sealed class FixedSessionBinding(string binding) : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(binding);
    }

    private sealed class RecordingService : ISalesReferenceReadService
    {
        public List<SalesAccountQueryV1> Queries { get; } = [];

        public Task<SalesReferenceReadResult<SalesAccountOptionsV1>> GetAccountsAsync(
            SalesAccountQueryV1 query, string sessionBinding, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new SalesReferenceReadResult<SalesAccountOptionsV1>(
                200, new(TestCompanyIds.CompanyA, [], null), null));
        }

        public Task<SalesReferenceReadResult<SalesCurrencyReferenceV1>> GetCurrencyAsync(
            SalesCurrencyQueryV1 query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SalesReferenceReadResult<SalesProductReferenceV1>> GetProductAsync(
            SalesProductQueryV1 query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SalesReferenceReadResult<SalesContactInformationOptionsV1>> GetContactInformationAsync(
            SalesContactInformationQueryV1 query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SalesReferenceReadResult<SalesCurrencyOptionsV1>> GetCurrenciesAsync(
            SalesCurrencyListQueryV1 query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
