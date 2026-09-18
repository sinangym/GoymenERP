using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The draft routes' HTTP boundary over a SENTETIK <see cref="ISalesDraftService"/> (no database,
/// no owner): what the wire carries for each service answer. The service's own rules are unit-tested
/// (<c>SalesDraftServiceTests</c>); the real host with the real store is the SQL Server class.
/// </summary>
public sealed class SalesDraftHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;

    [Theory]
    [InlineData("permission_unregistered")]
    [InlineData("counterparty_provider_unavailable")]
    [InlineData("settings_provider_unavailable")]
    [InlineData("reference_provider_invalid")]
    [InlineData("product_eligibility_unavailable")]
    [InlineData("service_unavailable")]
    public async Task A_503_of_the_draft_service_reaches_the_wire_only_as_the_platforms_public_code(string sourceCode)
    {
        var service = new StubDraftService { Create = _ => throw new SalesDraftServiceException(503, sourceCode) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var request = Post("SENTETIK-503");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("service_unavailable", document.RootElement.GetProperty("code").GetString());
        if (!string.Equals(sourceCode, "service_unavailable", StringComparison.Ordinal))
        {
            Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
        }

        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task A_created_draft_answers_201_with_its_absolute_detail_location_and_no_replay_header()
    {
        var draftId = Guid.NewGuid();
        var service = new StubDraftService { Create = _ => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), false, "SENTETIK-corr-1")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var request = Post("SENTETIK-created");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.DraftsPath.TrimStart('/')}/{draftId:D}"), response.Headers.Location);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(draftId, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid());
        Assert.Equal("SENTETIK-created", Assert.Single(service.Keys));
    }

    [Fact]
    public async Task A_replayed_draft_and_a_replayed_refusal_carry_the_replay_headers_of_the_first_attempt()
    {
        var draftId = Guid.NewGuid();
        var service = new StubDraftService { Create = _ => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), true, "SENTETIK-first")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var request = Post("SENTETIK-replay"))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.Null(response.Headers.Location);
        }

        service.Create = _ => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict,
            [new("lines[0].productId", "product_version_conflict")], isReplay: true, originalCorrelationId: "SENTETIK-first");
        using (var request = Post("SENTETIK-replay"))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, problem.GetProperty("code").GetString());
            Assert.Equal("lines[0].productId", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }
    }

    [Fact]
    public async Task A_missing_repeated_or_over_long_key_is_refused_before_the_service()
    {
        var service = new StubDraftService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var none = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() })
        using (var response = await client.SendAsync(none))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.CommandMetadataInvalid, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var twice = Post("SENTETIK-a"))
        {
            twice.Headers.Add("Idempotency-Key", "SENTETIK-b");
            using var response = await client.SendAsync(twice);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var over = Post(new string('k', 129)))
        using (var response = await client.SendAsync(over))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var exact = Post(new string('k', 128)))
        using (var response = await client.SendAsync(exact))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        Assert.Equal(new[] { new string('k', 128) }, service.Keys);
    }

    private static HttpRequestMessage Post(string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static StringContent Body()
    {
        var line = new SalesOrderDraftLineRequest(Guid.NewGuid(), Guid.NewGuid(), "1", Guid.NewGuid(), "1.25", null, null);
        var request = new CreateSalesOrderDraftRequest("Order", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 18), Guid.NewGuid(), [line]);
        return new StringContent(JsonSerializer.Serialize(request, SalesDraftJson.Options), Encoding.UTF8, "application/json");
    }

    private static SalesOrderDraftDetailView Detail(Guid draftId) => new(
        new SalesOrderDraftSummaryView(draftId, CompanyA, "SO-2026-000001", "Order", "Draft", "1", "Rev.0", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 9, 18), new DateTimeOffset(2026, 9, 18, 14, 0, 0, TimeSpan.Zero), 0),
        null, null, [], null, null, null, null, null, null);

    private WebApplicationFactory<Program> CreateHost(ISalesDraftService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesDraftService>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private sealed class StubDraftService : ISalesDraftService
    {
        public List<string> Keys { get; } = [];

        public Func<CreateSalesOrderDraftRequest, Task<SalesOrderDraftResponse>> Create { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftResponse(Detail(Guid.NewGuid()), false, "SENTETIK-corr"));

        public Task<SalesOrderDraftResponse> CreateDraftAsync(CreateSalesOrderDraftRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(metadata.IdempotencyKey);
            return Create(request);
        }

        public Task<SalesOrderDraftDetailView> GetDraftDetailAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            throw new SalesDraftServiceException(404, "not_found");

        public Task<SalesOrderDraftListResponse> GetDraftListAsync(SalesOrderDraftListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderDraftListResponse([], null));
    }
}
