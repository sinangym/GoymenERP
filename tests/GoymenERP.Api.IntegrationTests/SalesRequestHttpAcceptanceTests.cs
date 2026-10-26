using System.Net;
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
/// API-20 / API-21: the sales request routes' HTTP boundary over a SENTETIK <see cref="ISalesRequestService"/> (no database,
/// no owner): the metadata and body checks that answer before the service, the 201/200 + replay headers, the absolute
/// <c>Location</c>, the strict query of the list, and the problem shapes (401/403/404/503 code-less). The service's own rules
/// are unit-tested; the real host with the real store is the SQL Server class. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesRequestHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;

    [Fact]
    public async Task A_created_request_answers_201_with_its_absolute_location_and_a_replay_carries_the_replay_headers()
    {
        var requestId = Guid.NewGuid();
        var service = new StubRequestService { Create = _ => Task.FromResult(new SalesRequestResponse(Detail(requestId), false, "SENTETIK-corr-1")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.SendAsync(Post("SENTETIK-created")))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.RequestsPath.TrimStart('/')}/{requestId:D}"), response.Headers.Location);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(requestId, body.RootElement.GetProperty("detail").GetProperty("request").GetProperty("id").GetGuid());
            Assert.Equal("Submitted", body.RootElement.GetProperty("detail").GetProperty("request").GetProperty("state").GetString());
        }

        service.Create = _ => Task.FromResult(new SalesRequestResponse(Detail(requestId), true, "SENTETIK-corr-1"));
        using (var replay = await client.SendAsync(Post("SENTETIK-created")))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-corr-1", Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        Assert.Equal(["SENTETIK-created", "SENTETIK-created"], service.Keys);
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("blank-key")]
    [InlineData("comma-key")]
    [InlineData("not-json")]
    [InlineData("unknown-member")]
    [InlineData("no-lines")]
    [InlineData("bad-quantity")]
    [InlineData("empty-party")]
    public async Task The_metadata_and_the_strict_body_answer_before_the_service(string fault)
    {
        var service = new StubRequestService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var json = JsonSerializer.Serialize(Create(), SalesDraftJson.Options);
        json = fault switch
        {
            "unknown-member" => json.Replace("\"partyId\"", "\"price\":\"1\",\"partyId\"", StringComparison.Ordinal),
            "no-lines" => JsonSerializer.Serialize(Create() with { Lines = [] }, SalesDraftJson.Options),
            "bad-quantity" => JsonSerializer.Serialize(Create("1,25"), SalesDraftJson.Options),
            "empty-party" => JsonSerializer.Serialize(Create() with { PartyId = Guid.Empty }, SalesDraftJson.Options),
            _ => json,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.RequestsPath)
        {
            Content = new StringContent(json, Encoding.UTF8, fault == "not-json" ? "text/plain" : "application/json"),
        };
        if (fault != "missing-key")
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", fault switch { "blank-key" => " ", "comma-key" => "a,b", _ => "SENTETIK-k" });
        }

        using var response = await client.SendAsync(request);

        if (fault == "not-json")
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(fault.EndsWith("-key", StringComparison.Ordinal) ? "command_metadata_invalid" : "validation_failed",
                body.RootElement.GetProperty("code").GetString());
        }

        Assert.Empty(service.Keys);
    }

    [Theory]
    [InlineData(503, "permission_unregistered")]
    [InlineData(403, "forbidden")]
    [InlineData(409, "account_state")]
    [InlineData(422, "unsupported_product")]
    public async Task The_services_refusal_reaches_the_wire_with_its_code_and_every_403_or_503_code_less(int status, string code)
    {
        var service = new StubRequestService
        {
            Create = _ => throw new SalesDraftServiceException(status, code, status == 422 ? [new("lines[0].productId", "product_not_sellable")] : null),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post("SENTETIK-refused"));

        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        if (status is 403 or 503)
        {
            Assert.DoesNotContain(code == "forbidden" ? "\"errors\"" : code, body, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
            if (status == 422)
            {
                Assert.Equal("lines[0].productId", document.RootElement.GetProperty("errors")[0].GetProperty("field").GetString());
            }
        }
    }

    [Fact]
    public async Task The_list_takes_only_its_three_single_queries_and_the_detail_of_another_company_is_a_code_less_404()
    {
        var service = new StubRequestService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var ok = await client.GetAsync($"{SalesEndpoints.RequestsPath}?pageSize=20&state=Approved"))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        Assert.Equal(new SalesRequestListQuery(20, null, "Approved"), Assert.Single(service.Queries));
        foreach (var query in new[] { "?approvalState=Approved", "?pageSize=0", "?pageSize=101", "?state=", "?state=A&state=B" })
        {
            using var refused = await client.GetAsync(SalesEndpoints.RequestsPath + query);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        Assert.Single(service.Queries);
        using var missing = await client.GetAsync($"{SalesEndpoints.RequestsPath}/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.DoesNotContain("not_found", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_decision_answers_200_takes_a_reason_only_on_the_rejection_and_a_replayed_refusal_keeps_its_headers()
    {
        var requestId = Guid.NewGuid();
        var service = new StubRequestService
        {
            Decide = (segment, _, body) => segment == "approval"
                ? Task.FromResult(new SalesRequestResponse(Detail(requestId, "Approved"), false, "SENTETIK-corr"))
                : throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.StateConflict, [new("state", "sales_state_conflict")],
                    isReplay: true, originalCorrelationId: "SENTETIK-first"),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var approved = await client.SendAsync(Decision(requestId, "approval", "SENTETIK-a", "{\"expectedVersion\":\"1\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            using var body = JsonDocument.Parse(await approved.Content.ReadAsStringAsync());
            Assert.Equal("Approved", body.RootElement.GetProperty("detail").GetProperty("request").GetProperty("state").GetString());
        }

        using (var reasoned = await client.SendAsync(Decision(requestId, "approval", "SENTETIK-b", "{\"expectedVersion\":\"1\",\"reason\":\"x\"}")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, reasoned.StatusCode);
        }

        using (var badVersion = await client.SendAsync(Decision(requestId, "rejection", "SENTETIK-c", "{\"expectedVersion\":\"01\",\"reason\":\"x\"}")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badVersion.StatusCode);
        }

        using (var replayed = await client.SendAsync(Decision(requestId, "rejection", "SENTETIK-d", "{\"expectedVersion\":\"2\",\"reason\":\"Vazgeçildi\"}")))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(replayed.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            using var body = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync());
            Assert.Equal(SalesDraftProblemCodesV1.StateConflict, body.RootElement.GetProperty("code").GetString());
        }

        Assert.Equal([("approval", "1", (string?)null), ("rejection", "2", "Vazgeçildi")],
            service.Decisions.Select(decision => (decision.Segment, decision.Body.ExpectedVersion, decision.Body.Reason)));
    }

    private WebApplicationFactory<Program> CreateHost(ISalesRequestService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesRequestService>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private static CreateSalesRequestRequest Create(string quantity = "1.25") => new(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 26),
        [new SalesRequestLineRequest(Guid.NewGuid(), Guid.NewGuid(), "7", Guid.NewGuid(), quantity)]);

    private static HttpRequestMessage Post(string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.RequestsPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(Create(), SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static HttpRequestMessage Decision(Guid requestId, string segment, string idempotencyKey, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.RequestsPath}/{requestId:D}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static SalesRequestDetailView Detail(Guid requestId, string state = "Submitted") => new(
        new SalesRequestSummaryView(requestId, CompanyA, "SR-2026-000001", state, state == "Submitted" ? "1" : "2", Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 10, 26), new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), 1),
        null, null,
        [new SalesRequestLineView(Guid.NewGuid(), 1, Guid.NewGuid(), "7", "GDC-001", "Sentetik bant", Guid.NewGuid(), "kg", "1.25", null, null)],
        state == "Submitted" ? null : new SalesRequestDecisionV1("SENTETIK-approver", new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), null));

    private sealed class StubRequestService : ISalesRequestService
    {
        public List<string> Keys { get; } = [];
        public List<SalesRequestListQuery> Queries { get; } = [];
        public List<(string Segment, SalesDraftStateCommandRequest Body)> Decisions { get; } = [];

        public Func<CreateSalesRequestRequest, Task<SalesRequestResponse>> Create { get; set; } =
            _ => Task.FromResult(new SalesRequestResponse(Detail(Guid.NewGuid()), false, "SENTETIK-corr"));

        public Func<string, Guid, SalesDraftStateCommandRequest, Task<SalesRequestResponse>> Decide { get; set; } =
            (_, id, _) => Task.FromResult(new SalesRequestResponse(Detail(id, "Approved"), false, "SENTETIK-corr"));

        public Task<SalesRequestResponse> CreateRequestAsync(CreateSalesRequestRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(metadata.IdempotencyKey);
            return Create(request);
        }

        public Task<SalesRequestListResponse> GetRequestListAsync(SalesRequestListQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new SalesRequestListResponse([], null));
        }

        public Task<SalesRequestDetailView> GetRequestDetailAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromException<SalesRequestDetailView>(new SalesDraftServiceException(404, "not_found"));

        public Task<SalesRequestResponse> ApproveRequestAsync(Guid requestId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Decisions.Add(("approval", request));
            return Decide("approval", requestId, request);
        }

        public Task<SalesRequestResponse> RejectRequestAsync(Guid requestId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Decisions.Add(("rejection", request));
            return Decide("rejection", requestId, request);
        }
    }
}
