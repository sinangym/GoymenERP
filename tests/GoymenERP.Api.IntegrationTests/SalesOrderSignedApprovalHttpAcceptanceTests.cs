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
/// WP13 (YOL-D11): the order's signed approval routes' HTTP boundary over a SENTETIK <see cref="ISalesOrderSignedApprovalServiceV1"/>
/// (no database): the record answers 201 and the same key again carries the replay headers (idempotent binding); the body is strict;
/// the metadata and media type answer before the service; an unauthenticated caller or another company reaches nothing; 403 / 404
/// stay code-less; a recorded refusal is replayed with its code. The service's rules are unit-tested; the store is the SQL Server class.
/// </summary>
public sealed class SalesOrderSignedApprovalHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid DocumentId = Guid.Parse("d0c0d0c0-d0c0-4d0c-8d0c-d0c0d0c0d0c0");

    [Fact]
    public async Task A_record_answers_201_and_the_same_key_again_carries_the_replay_headers_without_a_second_record()
    {
        var orderId = Guid.NewGuid();
        var service = new StubService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var json = $"{{\"documentId\":\"{DocumentId:D}\",\"expectedOrderVersion\":\"7\"}}";

        using (var response = await client.SendAsync(Post(orderId, json, "SENTETIK-signed-1")))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("signedApprovalReceived").GetBoolean());
            Assert.Equal(DocumentId, body.RootElement.GetProperty("active").GetProperty("documentId").GetGuid());
        }

        using (var again = await client.SendAsync(Post(orderId, json, "SENTETIK-signed-1")))
        {
            Assert.Equal(HttpStatusCode.Created, again.StatusCode);
            Assert.Equal("true", Assert.Single(again.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-corr-signed", Assert.Single(again.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        Assert.Equal([$"record:{orderId:D}:{DocumentId:D}:7:SENTETIK-signed-1", $"record:{orderId:D}:{DocumentId:D}:7:SENTETIK-signed-1"],
            service.Calls);
        Assert.Equal(1, service.Recorded);
    }

    [Theory]
    [InlineData("{\"documentId\":\"d0c0d0c0-d0c0-4d0c-8d0c-d0c0d0c0d0c0\"}")]
    [InlineData("{\"expectedOrderVersion\":\"7\"}")]
    [InlineData("{\"documentId\":\"00000000-0000-0000-0000-000000000000\",\"expectedOrderVersion\":\"7\"}")]
    [InlineData("{\"documentId\":\"d0c0d0c0-d0c0-4d0c-8d0c-d0c0d0c0d0c0\",\"expectedOrderVersion\":\"07\"}")]
    [InlineData("{\"documentId\":\"d0c0d0c0-d0c0-4d0c-8d0c-d0c0d0c0d0c0\",\"expectedOrderVersion\":\"7\",\"extra\":1}")]
    [InlineData("null")]
    public async Task A_record_body_that_is_not_strict_answers_400_before_the_service(string json)
    {
        var service = new StubService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(Guid.NewGuid(), json, "SENTETIK-strict"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("blank-key")]
    [InlineData("text-plain")]
    public async Task The_metadata_and_media_type_answer_before_the_service(string fault)
    {
        var service = new StubService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var request = new HttpRequestMessage(HttpMethod.Post, Path(Guid.NewGuid()))
        {
            Content = new StringContent($"{{\"documentId\":\"{DocumentId:D}\",\"expectedOrderVersion\":\"7\"}}", Encoding.UTF8,
                fault == "text-plain" ? "text/plain" : "application/json"),
        };
        if (fault != "missing-key")
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", fault == "blank-key" ? " " : "SENTETIK-k");
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(fault == "text-plain" ? HttpStatusCode.UnsupportedMediaType : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task An_anonymous_caller_or_another_company_reaches_neither_the_record_nor_the_read()
    {
        var service = new StubService();
        using var host = CreateHost(service);
        var orderId = Guid.NewGuid();

        using (var anonymous = host.CreateClient())
        {
            anonymous.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var read = await anonymous.GetAsync(Path(orderId));
            using var record = await anonymous.SendAsync(Post(orderId, $"{{\"documentId\":\"{DocumentId:D}\",\"expectedOrderVersion\":\"7\"}}", "SENTETIK-a"));
            Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (read.StatusCode, record.StatusCode));
        }

        using (var foreign = host.CreateClient())
        {
            foreign.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyB);
            foreign.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
            using var read = await foreign.GetAsync(Path(orderId));
            using var record = await foreign.SendAsync(Post(orderId, $"{{\"documentId\":\"{DocumentId:D}\",\"expectedOrderVersion\":\"7\"}}", "SENTETIK-f"));
            Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (read.StatusCode, record.StatusCode));
        }

        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData(403, HttpStatusCode.Forbidden)]
    [InlineData(404, HttpStatusCode.NotFound)]
    [InlineData(503, HttpStatusCode.ServiceUnavailable)]
    public async Task A_service_refusal_of_authority_existence_or_availability_is_code_less(int status, HttpStatusCode expected)
    {
        var service = new StubService { Refusal = new SalesDraftServiceException(status, "SENTETIK-source") };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var read = await client.GetAsync(Path(Guid.NewGuid()));
        using var record = await client.SendAsync(Post(Guid.NewGuid(), $"{{\"documentId\":\"{DocumentId:D}\",\"expectedOrderVersion\":\"7\"}}", "SENTETIK-r"));

        Assert.Equal((expected, expected), (read.StatusCode, record.StatusCode));
        Assert.DoesNotContain("SENTETIK-source", await read.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("SENTETIK-source", await record.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_recorded_refusal_and_an_invalid_document_answer_their_codes_and_a_replayed_refusal_its_headers()
    {
        var service = new StubService
        {
            Refusal = new SalesDraftServiceException(409, SalesOrderSignedApprovalProblemCodesV1.AlreadyCurrent,
                [new SalesDraftFieldError("documentId", SalesOrderSignedApprovalProblemCodesV1.AlreadyCurrent)], isReplay: true,
                originalCorrelationId: "SENTETIK-corr-first"),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var json = $"{{\"documentId\":\"{DocumentId:D}\",\"expectedOrderVersion\":\"7\"}}";

        using (var replayed = await client.SendAsync(Post(Guid.NewGuid(), json, "SENTETIK-again")))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
            using var body = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync());
            Assert.Equal(SalesOrderSignedApprovalProblemCodesV1.AlreadyCurrent, body.RootElement.GetProperty("code").GetString());
        }

        service.Refusal = new SalesDraftServiceException(422, SalesOrderSignedApprovalProblemCodesV1.DocumentInvalid,
            [new SalesDraftFieldError("documentId", "document_not_on_order")]);
        using (var invalid = await client.SendAsync(Post(Guid.NewGuid(), json, "SENTETIK-invalid")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
            using var body = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
            Assert.Equal(SalesOrderSignedApprovalProblemCodesV1.DocumentInvalid, body.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task A_withdrawal_takes_exactly_the_expected_version_and_a_reason_and_answers_200()
    {
        var orderId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var service = new StubService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var withdrawn = await client.SendAsync(Deactivate(orderId, approvalId, "{\"expectedVersion\":\"1\",\"reason\":\"Yanlış dosya\"}", "SENTETIK-w"));
        using var noReason = await client.SendAsync(Deactivate(orderId, approvalId, "{\"expectedVersion\":\"1\"}", "SENTETIK-w2"));
        using var extra = await client.SendAsync(Deactivate(orderId, approvalId, "{\"expectedVersion\":\"1\",\"reason\":\"x\",\"extra\":1}", "SENTETIK-w3"));

        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (noReason.StatusCode, extra.StatusCode));
        Assert.Equal([$"deactivate:{orderId:D}:{approvalId:D}:1:Yanlış dosya"], service.Calls);
    }

    private static string Path(Guid orderId) => $"{SalesEndpoints.DraftsPath}/{orderId:D}/{SalesOrderSignedApprovalApiV1.Segment}";

    private static HttpRequestMessage Post(Guid orderId, string json, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path(orderId))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static HttpRequestMessage Deactivate(Guid orderId, Guid approvalId, string json, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{Path(orderId)}/{approvalId:D}/{SalesOrderSignedApprovalApiV1.DeactivationSegment}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private WebApplicationFactory<Program> CreateHost(ISalesOrderSignedApprovalServiceV1 service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesOrderSignedApprovalServiceV1>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private static SalesOrderSignedApprovalsResponseV1 Status(Guid orderId)
    {
        var active = new SalesOrderSignedApprovalViewV1(Guid.NewGuid(), orderId, "Rev.3", DocumentId, 2, new string('a', 64), "Active", "1",
            "SENTETIK-seller", DateTimeOffset.UnixEpoch, null, null, null, null, null);
        return new SalesOrderSignedApprovalsResponseV1(orderId, "7", "Rev.3", true, active, [active]);
    }

    /// <summary>Records per key once (the real store's identity), answers the same key again as a replay.</summary>
    private sealed class StubService : ISalesOrderSignedApprovalServiceV1
    {
        private readonly HashSet<string> keys = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = [];
        public int Recorded { get; private set; }
        public SalesDraftServiceException? Refusal { get; set; }

        public Task<SalesOrderSignedApprovalCommandResultV1> RecordAsync(Guid orderId, RecordSalesOrderSignedApprovalRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls.Add($"record:{orderId:D}:{request.DocumentId:D}:{request.ExpectedOrderVersion}:{metadata.IdempotencyKey}");
            if (Refusal is not null)
            {
                throw Refusal;
            }

            var replay = !keys.Add(metadata.IdempotencyKey);
            if (!replay)
            {
                Recorded++;
            }

            return Task.FromResult(new SalesOrderSignedApprovalCommandResultV1(Status(orderId), replay,
                replay ? "SENTETIK-corr-signed" : metadata.CorrelationId));
        }

        public Task<SalesOrderSignedApprovalCommandResultV1> DeactivateAsync(Guid orderId, Guid approvalId,
            DeactivateSalesOrderSignedApprovalRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls.Add($"deactivate:{orderId:D}:{approvalId:D}:{request.ExpectedVersion}:{request.Reason}");
            return Task.FromResult(new SalesOrderSignedApprovalCommandResultV1(Status(orderId), false, metadata.CorrelationId));
        }

        public Task<SalesOrderSignedApprovalsResponseV1> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            if (Refusal is not null)
            {
                throw Refusal;
            }

            Calls.Add($"get:{orderId:D}");
            return Task.FromResult(Status(orderId));
        }
    }
}
