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
/// API-22…24: the sales quote routes' HTTP boundary over a SENTETIK <see cref="ISalesQuoteService"/> (no database, no owner):
/// the metadata and strict body checks that answer before the service, the 201 + the quote's absolute <c>Location</c>, the
/// conversion's 201 + the ORDER's <c>Location</c> and its replay headers, the strict list query, and the problem shapes.
/// The service's rules are unit-tested; the real host with the real store is the SQL Server class. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesQuoteHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;

    [Fact]
    public async Task A_created_quote_answers_201_with_its_absolute_location_and_a_replay_carries_the_replay_headers()
    {
        var quoteId = Guid.NewGuid();
        var service = new StubQuoteService { Create = _ => Task.FromResult(new SalesQuoteResponse(Detail(quoteId), false, "SENTETIK-corr-1")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.SendAsync(Post(SalesEndpoints.QuotesPath, "SENTETIK-created", Json(Create()))))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.QuotesPath.TrimStart('/')}/{quoteId:D}"), response.Headers.Location);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var quote = body.RootElement.GetProperty("detail").GetProperty("quote");
            Assert.Equal((quoteId, "Open", "Rev.0"), (quote.GetProperty("id").GetGuid(), quote.GetProperty("state").GetString(), quote.GetProperty("revision").GetString()));
        }

        service.Create = _ => Task.FromResult(new SalesQuoteResponse(Detail(quoteId), true, "SENTETIK-corr-1"));
        using (var replay = await client.SendAsync(Post(SalesEndpoints.QuotesPath, "SENTETIK-created", Json(Create()))))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-corr-1", Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        Assert.Equal(["SENTETIK-created", "SENTETIK-created"], service.Keys);
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("comma-key")]
    [InlineData("not-json")]
    [InlineData("unknown-member")]
    [InlineData("no-lines")]
    [InlineData("bad-quantity")]
    [InlineData("price-without-unit")]
    [InlineData("duplicate-request")]
    [InlineData("empty-currency")]
    public async Task The_metadata_and_the_strict_create_body_answer_before_the_service(string fault)
    {
        var service = new StubQuoteService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var duplicate = Guid.NewGuid();
        var json = fault switch
        {
            "unknown-member" => Json(Create()).Replace("\"partyId\"", "\"total\":\"1\",\"partyId\"", StringComparison.Ordinal),
            "no-lines" => Json(Create() with { Lines = [] }),
            "bad-quantity" => Json(Create(quantity: "1,25")),
            "price-without-unit" => Json(Create() with { Lines = [Line() with { PriceUnitId = null }] }),
            "duplicate-request" => Json(Create() with { RequestIds = [duplicate, duplicate] }),
            "empty-currency" => Json(Create() with { CurrencyId = Guid.Empty }),
            _ => Json(Create()),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.QuotesPath)
        {
            Content = new StringContent(json, Encoding.UTF8, fault == "not-json" ? "text/plain" : "application/json"),
        };
        if (fault != "missing-key")
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", fault == "comma-key" ? "a,b" : "SENTETIK-k");
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

    [Fact]
    public async Task A_conversion_answers_201_with_the_orders_location_and_its_replay_the_same_order_with_the_replay_headers()
    {
        var quoteId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var service = new StubQuoteService { Convert = (_, _) => Task.FromResult(new SalesOrderDraftResponse(DraftDetail(draftId), false, "SENTETIK-conv")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var path = $"{SalesEndpoints.QuotesPath}/{quoteId:D}/conversion";
        const string body = "{\"expectedVersion\":\"2\",\"businessDate\":\"2026-10-27\"}";

        using (var converted = await client.SendAsync(Post(path, "SENTETIK-convert", body)))
        {
            Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.DraftsPath.TrimStart('/')}/{draftId:D}"), converted.Headers.Location);
            using var document = JsonDocument.Parse(await converted.Content.ReadAsStringAsync());
            Assert.Equal(draftId, document.RootElement.GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid());
        }

        service.Convert = (_, _) => Task.FromResult(new SalesOrderDraftResponse(DraftDetail(draftId), true, "SENTETIK-conv"));
        using (var replay = await client.SendAsync(Post(path, "SENTETIK-convert", body)))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.DraftsPath.TrimStart('/')}/{draftId:D}"), replay.Headers.Location);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-conv", Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        foreach (var bad in new[] { "{\"expectedVersion\":\"02\",\"businessDate\":\"2026-10-27\"}", "{\"expectedVersion\":\"2\"}",
                     "{\"expectedVersion\":\"2\",\"businessDate\":\"2026-10-27\",\"lines\":[]}" })
        {
            using var refused = await client.SendAsync(Post(path, "SENTETIK-bad", bad));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        Assert.Equal([(quoteId, "2"), (quoteId, "2")], service.Conversions.Select(conversion => (conversion.QuoteId, conversion.Body.ExpectedVersion)));
    }

    [Fact]
    public async Task A_revision_and_a_loss_answer_200_and_a_replayed_refusal_keeps_its_code_and_headers()
    {
        var quoteId = Guid.NewGuid();
        var service = new StubQuoteService
        {
            Lose = (_, _) => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.StateConflict, [new("state", "sales_state_conflict")],
                isReplay: true, originalCorrelationId: "SENTETIK-first"),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        var revision = Json(new SalesQuoteRevisionRequest("1", [Line()], new DateOnly(2026, 11, 15)));
        using (var revised = await client.SendAsync(Post($"{SalesEndpoints.QuotesPath}/{quoteId:D}/revision", "SENTETIK-r", revision)))
        {
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
            Assert.Null(revised.Headers.Location);
        }

        using (var noReason = await client.SendAsync(Post($"{SalesEndpoints.QuotesPath}/{quoteId:D}/loss", "SENTETIK-l0", "{\"expectedVersion\":\"2\"}")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        }

        using (var replayed = await client.SendAsync(Post($"{SalesEndpoints.QuotesPath}/{quoteId:D}/loss", "SENTETIK-l1",
                   "{\"expectedVersion\":\"2\",\"reason\":\"Fiyat yüksek\"}")))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(replayed.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            using var body = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync());
            Assert.Equal(SalesDraftProblemCodesV1.StateConflict, body.RootElement.GetProperty("code").GetString());
        }

        Assert.Equal(["SENTETIK-r", "SENTETIK-l1"], service.Keys);
        Assert.Equal("Fiyat yüksek", Assert.Single(service.Losses).Reason);
    }

    [Theory]
    [InlineData(503, "permission_unregistered")]
    [InlineData(403, "forbidden")]
    [InlineData(403, "price_read_required")]
    [InlineData(409, "sales_quote_request_not_approved")]
    [InlineData(422, "sales_quote_request_customer_mismatch")]
    public async Task The_services_refusal_reaches_the_wire_with_its_code_and_every_403_or_503_code_less(int status, string code)
    {
        var service = new StubQuoteService { Convert = (_, _) => throw new SalesDraftServiceException(status, code) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post($"{SalesEndpoints.QuotesPath}/{Guid.NewGuid():D}/conversion", "SENTETIK-refused",
            "{\"expectedVersion\":\"1\",\"businessDate\":\"2026-10-27\"}"));

        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (status is 403 or 503)
        {
            // A 403's problem type names the status itself; what it must not carry is field errors.
            Assert.DoesNotContain(code == "forbidden" ? "\"errors\"" : code, body, StringComparison.Ordinal);
        }
        else
        {
            using var document = JsonDocument.Parse(body);
            Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task The_list_takes_only_its_three_single_queries_and_the_detail_of_another_company_is_a_code_less_404()
    {
        var service = new StubQuoteService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var ok = await client.GetAsync($"{SalesEndpoints.QuotesPath}?pageSize=20&state=Open"))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.True(ok.Headers.CacheControl is { NoStore: true, Private: true });
        }

        Assert.Equal(new SalesQuoteListQuery(20, null, "Open"), Assert.Single(service.Queries));
        foreach (var query in new[] { "?approvalState=Open", "?pageSize=0", "?pageSize=101", "?state=", "?state=A&state=B" })
        {
            using var refused = await client.GetAsync(SalesEndpoints.QuotesPath + query);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        Assert.Single(service.Queries);
        using var missing = await client.GetAsync($"{SalesEndpoints.QuotesPath}/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.DoesNotContain("not_found", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateHost(ISalesQuoteService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesQuoteService>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, SalesDraftJson.Options);

    private static SalesQuoteLineRequest Line(string quantity = "1.25") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "7", Guid.NewGuid(), quantity, "12.50", Guid.NewGuid(), "5");

    private static CreateSalesQuoteRequest Create(string quantity = "1.25") => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new DateOnly(2026, 10, 27), [Line(quantity)], [Guid.NewGuid()], new DateOnly(2026, 11, 10));

    private static HttpRequestMessage Post(string path, string idempotencyKey, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static SalesQuoteDetailView Detail(Guid quoteId, string state = "Open") => new(
        new SalesQuoteSummaryView(quoteId, CompanyA, "SQ-2026-000001", state, "1", "Rev.0", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 10), new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero), 1, null),
        [Guid.NewGuid()], null, null,
        [new SalesQuoteLineView(Guid.NewGuid(), 1, Guid.NewGuid(), "7", "GDC-001", "Sentetik bant", Guid.NewGuid(), "kg", "1.25",
            "12.50", Guid.NewGuid(), "5", SalesPriceDisclosure.Visible, null, null)],
        null, null);

    private static SalesOrderDraftDetailView DraftDetail(Guid draftId) => new(
        new SalesOrderDraftSummaryView(draftId, CompanyA, "SO-2026-000001", "Order", "Draft", "1", "Rev.0", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 10, 27), new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero), 1, "Preparing"),
        null, null, [], null, null, null, null, null, null, new SalesDraftApprovalV1(null, null, null, null, null),
        new SalesDraftCustomerAcceptanceV1(false, null, null, null, false), false, false);

    private sealed class StubQuoteService : ISalesQuoteService
    {
        public List<string> Keys { get; } = [];
        public List<SalesQuoteListQuery> Queries { get; } = [];
        public List<SalesQuoteLossRequest> Losses { get; } = [];
        public List<(Guid QuoteId, SalesQuoteConversionRequest Body)> Conversions { get; } = [];

        public Func<CreateSalesQuoteRequest, Task<SalesQuoteResponse>> Create { get; set; } =
            _ => Task.FromResult(new SalesQuoteResponse(Detail(Guid.NewGuid()), false, "SENTETIK-corr"));

        public Func<Guid, SalesQuoteLossRequest, Task<SalesQuoteResponse>> Lose { get; set; } =
            (id, _) => Task.FromResult(new SalesQuoteResponse(Detail(id, "Lost"), false, "SENTETIK-corr"));

        public Func<Guid, SalesQuoteConversionRequest, Task<SalesOrderDraftResponse>> Convert { get; set; } =
            (_, _) => Task.FromResult(new SalesOrderDraftResponse(DraftDetail(Guid.NewGuid()), false, "SENTETIK-corr"));

        public Task<SalesQuoteResponse> CreateQuoteAsync(CreateSalesQuoteRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(metadata.IdempotencyKey);
            return Create(request);
        }

        public Task<SalesQuoteListResponse> GetQuoteListAsync(SalesQuoteListQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new SalesQuoteListResponse([], null));
        }

        public Task<SalesQuoteDetailView> GetQuoteDetailAsync(Guid quoteId, CancellationToken cancellationToken = default) =>
            Task.FromException<SalesQuoteDetailView>(new SalesDraftServiceException(404, "not_found"));

        public Task<SalesQuoteResponse> ReviseQuoteAsync(Guid quoteId, SalesQuoteRevisionRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(metadata.IdempotencyKey);
            return Task.FromResult(new SalesQuoteResponse(Detail(quoteId), false, "SENTETIK-corr"));
        }

        public Task<SalesQuoteResponse> RecordQuoteLossAsync(Guid quoteId, SalesQuoteLossRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(metadata.IdempotencyKey);
            Losses.Add(request);
            return Lose(quoteId, request);
        }

        public Task<SalesOrderDraftResponse> ConvertQuoteAsync(Guid quoteId, SalesQuoteConversionRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Conversions.Add((quoteId, request));
            return Convert(quoteId, request);
        }
    }
}
