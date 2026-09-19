using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>The loading-instruction routes' wire contract over a stub service (the Sales HTTP acceptance pattern): metadata, statuses, headers, code boundary.</summary>
public sealed class LogisticsInstructionHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;

    [Theory]
    [InlineData("permission_unregistered")]
    [InlineData("counterparty_provider_unavailable")]
    [InlineData("sales_provider_unavailable")]
    [InlineData("reference_provider_invalid")]
    [InlineData("service_unavailable")]
    public async Task A_503_of_the_service_reaches_the_wire_only_as_the_platforms_public_code(string sourceCode)
    {
        var service = new StubService { Create = () => throw new LogisticsInstructionServiceException(503, sourceCode) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(string.Empty, "SENTETIK-503", CreateBody()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("service_unavailable", JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        if (sourceCode != "service_unavailable")
        {
            Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_created_instruction_answers_201_with_its_location_and_a_replay_carries_the_first_attempts_headers()
    {
        var id = Guid.NewGuid();
        var service = new StubService { Create = () => Task.FromResult(new LoadingInstructionResponse(Detail(id), false, "SENTETIK-corr-1")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.SendAsync(Post(string.Empty, "SENTETIK-created", CreateBody())))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{LogisticsEndpoints.InstructionsPath.TrimStart('/')}/{id:D}"), response.Headers.Location);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.Equal(id, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("instruction").GetProperty("id").GetGuid());
        }

        service.Create = () => Task.FromResult(new LoadingInstructionResponse(Detail(id), true, "SENTETIK-first"));
        using (var response = await client.SendAsync(Post(string.Empty, "SENTETIK-created", CreateBody())))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        service.Load = () => throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.LoadExceedsInstructed,
            [new("quantity", "logistics_load_exceeds_instructed")], isReplay: true, originalCorrelationId: "SENTETIK-first");
        using (var response = await client.SendAsync(Post($"{id:D}/lines/{Guid.NewGuid():D}/loads", "SENTETIK-load",
                   "{\"expectedVersion\":\"3\",\"loadId\":\"" + Guid.NewGuid().ToString("D") + "\",\"quantity\":\"1\",\"measurement\":\"Measured\"}")))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(LogisticsProblemCodesV1.LoadExceedsInstructed, problem.GetProperty("code").GetString());
            Assert.Equal("quantity", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }
    }

    [Fact]
    public async Task The_approved_order_lines_read_answers_the_price_free_page_and_refuses_a_loose_query_before_the_service()
    {
        // LOG-4b, Sinan §4 (#66 5738113623): the picker's read carries no price member on the wire — the JSON keys are pinned here.
        var service = new StubService
        {
            ApprovedLines = query => Task.FromResult(new LogisticsApprovedOrderLinePage(
                [new LogisticsApprovedOrderLineView(Guid.NewGuid(), "SENTETIK SO-0001", Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "Sentetik Müşteri",
                    Guid.NewGuid(), "7", "GDC-001", "Sentetik rulo", Guid.NewGuid(), "kg", "12.500", new DateOnly(2026, 9, 25), 0)],
                query.Cursor is null ? "SENTETIK-next" : null)),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.GetAsync($"{LogisticsEndpoints.InstructionsPath}/approved-order-lines?pageSize=10"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var item = Assert.Single(body.GetProperty("items").EnumerateArray());
            Assert.Equal(
                ["orderId", "documentNo", "lineId", "lineNo", "customerPartyId", "companyAccountId", "customerName", "productId", "productVersion", "productCode",
                    "productName", "unitId", "unitCode", "quantity", "requestedDeliveryDate", "revision"],
                item.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(("12.500", "SENTETIK-next"), (item.GetProperty("quantity").GetString(), body.GetProperty("nextCursor").GetString()));
        }

        using (var response = await client.GetAsync($"{LogisticsEndpoints.InstructionsPath}/approved-order-lines?pageSize=10&status=Draft"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task A_missing_key_a_wrong_media_type_and_a_loose_body_are_refused_before_the_service()
    {
        var service = new StubService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var none = new HttpRequestMessage(HttpMethod.Post, LogisticsEndpoints.InstructionsPath) { Content = new StringContent(CreateBody(), Encoding.UTF8, "application/json") })
        using (var response = await client.SendAsync(none))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(LogisticsProblemCodesV1.CommandMetadataInvalid, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var text = Post(string.Empty, "SENTETIK-text", CreateBody(), "text/plain"))
        using (var response = await client.SendAsync(text))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        using (var loose = Post($"{Guid.NewGuid():D}/issue", "SENTETIK-loose", "{\"expectedVersion\":\"1\",\"reason\":\"x\"}"))
        using (var response = await client.SendAsync(loose))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(0, service.Calls);
    }

    private static string CreateBody() =>
        $$"""{"customerPartyId":"{{Guid.NewGuid():D}}","companyAccountId":"{{Guid.NewGuid():D}}","effectiveOn":"2026-09-19","lines":[{"lineId":"{{Guid.NewGuid():D}}","sourceOrderId":"{{Guid.NewGuid():D}}","sourceOrderLineId":"{{Guid.NewGuid():D}}","unitId":"{{Guid.NewGuid():D}}","quantity":"6"}]}""";

    private static HttpRequestMessage Post(string segment, string key, string json, string mediaType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, segment.Length == 0 ? LogisticsEndpoints.InstructionsPath : $"{LogisticsEndpoints.InstructionsPath}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, mediaType),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static LoadingInstructionDetailView Detail(Guid id) => new(
        new LoadingInstructionSummaryView(id, CompanyA, "Draft", "2", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 19), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1),
        [new LoadingInstructionLineView(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(), "6", "0", "0", "0", "6", "0", "0", false)],
        []);

    private WebApplicationFactory<Program> CreateHost(StubService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<ILogisticsInstructionService>(_ => service))));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyA);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return client;
    }

    private sealed class StubService : ILogisticsInstructionService
    {
        public int Calls { get; private set; }
        public Func<Task<LoadingInstructionResponse>> Create { get; set; } = () => throw new InvalidOperationException("not expected");
        public Func<Task<LoadingInstructionResponse>> Load { get; set; } = () => throw new InvalidOperationException("not expected");

        public Task<LoadingInstructionResponse> CreateInstructionAsync(CreateLoadingInstructionRequest request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Create();
        }

        public Task<LoadingInstructionResponse> IssueInstructionAsync(Guid instructionId, IssueLoadingInstructionRequest request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new LogisticsInstructionServiceException(404, "not_found");
        }

        public Task<LoadingInstructionResponse> RecordLoadAsync(Guid instructionId, Guid lineId, RecordLoadRequest request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Load();
        }

        public Task<LoadingInstructionDetailView> GetInstructionDetailAsync(Guid instructionId, CancellationToken cancellationToken = default) =>
            throw new LogisticsInstructionServiceException(404, "not_found");

        public Task<LoadingInstructionListResponse> GetInstructionListAsync(LoadingInstructionListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoadingInstructionListResponse([], null));

        public Func<LogisticsApprovedOrderLinesQuery, Task<LogisticsApprovedOrderLinePage>> ApprovedLines { get; set; } =
            _ => Task.FromResult(new LogisticsApprovedOrderLinePage([], null));

        public Task<LogisticsApprovedOrderLinePage> GetApprovedOrderLinesAsync(LogisticsApprovedOrderLinesQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ApprovedLines(query);
        }
    }
}
