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
/// API-40 (SAT-D290 (2, 3)): the customer return rejection and cancellation routes' HTTP boundary over a SENTETIK
/// <see cref="ISalesCustomerReturnService"/> (no database): the decision body is strict and carries an optional reason, a step
/// route refuses a reason member (400 before the service), the metadata and media type answer before the service, a replay carries
/// the replay headers (a replayed self-rejection refusal too), and 403 / 404 stay code-less. The service's own rules are
/// unit-tested; the store is the SQL Server class. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesCustomerReturnHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;

    [Theory]
    [InlineData("rejection", "reject")]
    [InlineData("cancellation", "cancel")]
    public async Task A_decision_route_answers_200_passes_the_reason_and_a_replay_carries_the_replay_headers(string route, string command)
    {
        var returnId = Guid.NewGuid();
        var replay = false;
        var service = new StubCustomerReturnService
        {
            Decide = (_, id) => Task.FromResult(new SalesCustomerReturnCommandResult(View(id), replay, "SENTETIK-corr-return")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        const string json = "{\"expectedVersion\":\"2\",\"reason\":\"Belge eksik\"}";

        using (var response = await client.SendAsync(Post(returnId, route, json, "SENTETIK-decision")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(returnId, body.RootElement.GetProperty("id").GetGuid());
        }

        replay = true;
        using (var again = await client.SendAsync(Post(returnId, route, json, "SENTETIK-decision")))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal("true", Assert.Single(again.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-corr-return", Assert.Single(again.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        using (var noReason = await client.SendAsync(Post(returnId, route, "{\"expectedVersion\":\"2\"}", "SENTETIK-no-reason")))
        {
            Assert.Equal(HttpStatusCode.OK, noReason.StatusCode);
        }

        Assert.Equal([$"{command}:{returnId:D}:2:Belge eksik", $"{command}:{returnId:D}:2:Belge eksik", $"{command}:{returnId:D}:2:"],
            service.Bodies);
    }

    [Theory]
    [InlineData("rejection", "{\"expectedVersion\":\"2\",\"extra\":true}")]
    [InlineData("rejection", "{\"expectedVersion\":\"02\"}")]
    [InlineData("rejection", "{\"expectedVersion\":\"0\"}")]
    [InlineData("rejection", "{\"reason\":\"x\"}")]
    [InlineData("rejection", "{\"expectedVersion\":\"2\",\"reason\":1}")]
    [InlineData("cancellation", "{\"expectedVersion\":\"1\",\"extra\":true}")]
    [InlineData("cancellation", "{}")]
    [InlineData("cancellation", "null")]
    [InlineData("submission", "{\"expectedVersion\":\"1\",\"reason\":\"x\"}")]
    [InlineData("approval", "{\"expectedVersion\":\"2\",\"reason\":\"x\"}")]
    [InlineData("finalization", "{\"expectedVersion\":\"3\",\"reason\":\"x\"}")]
    public async Task A_customer_return_command_body_that_is_not_strict_answers_400_before_the_service(string route, string json)
    {
        var service = new StubCustomerReturnService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(Guid.NewGuid(), route, json, "SENTETIK-bad"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Empty(service.Bodies);
    }

    [Theory]
    [InlineData("rejection", "missing-key")]
    [InlineData("rejection", "comma-key")]
    [InlineData("rejection", "not-json")]
    [InlineData("cancellation", "missing-key")]
    [InlineData("cancellation", "blank-key")]
    [InlineData("cancellation", "not-json")]
    public async Task A_decision_without_its_metadata_or_as_another_media_type_answers_before_the_service(string route, string fault)
    {
        var service = new StubCustomerReturnService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.CustomerReturnsPath}/{Guid.NewGuid():D}/{route}")
        {
            Content = new StringContent("{\"expectedVersion\":\"2\"}", Encoding.UTF8, fault == "not-json" ? "text/plain" : "application/json"),
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
            Assert.Equal("command_metadata_invalid", body.RootElement.GetProperty("code").GetString());
        }

        Assert.Empty(service.Bodies);
    }

    [Theory]
    [InlineData("rejection", 403, "forbidden")]
    [InlineData("cancellation", 403, "forbidden")]
    [InlineData("rejection", 404, "not_found")]
    [InlineData("cancellation", 404, "not_found")]
    [InlineData("rejection", 503, "permission_unregistered")]
    public async Task A_decision_refused_403_404_or_503_by_the_service_is_code_less_on_the_wire(string route, int status, string code)
    {
        var service = new StubCustomerReturnService { Decide = (_, _) => throw new SalesDraftServiceException(status, code) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(Guid.NewGuid(), route, "{\"expectedVersion\":\"2\"}", "SENTETIK-refused"));

        Assert.Equal(status, (int)response.StatusCode);
        // The platform writer answers 403 with its own public "forbidden"; another company's return is a plain 404.
        if (code != "forbidden")
        {
            Assert.DoesNotContain(code, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("rejection", 409, "sales_customer_return_self_approval")]
    [InlineData("rejection", 409, "state_conflict")]
    [InlineData("cancellation", 409, "version_conflict")]
    [InlineData("cancellation", 422, "sales_approval_reason_too_long")]
    public async Task A_decision_refusal_carries_its_stable_code(string route, int status, string code)
    {
        var service = new StubCustomerReturnService { Decide = (_, _) => throw new SalesDraftServiceException(status, code) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(Guid.NewGuid(), route, "{\"expectedVersion\":\"2\"}", "SENTETIK-refused"));

        Assert.Equal(status, (int)response.StatusCode);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_recorded_self_rejection_answered_again_under_the_same_key_is_the_same_409_with_the_replay_headers()
    {
        var service = new StubCustomerReturnService
        {
            Decide = (_, _) => throw new SalesDraftServiceException(409, SalesCustomerReturnProblemCodesV1.SelfApproval, null, true,
                "SENTETIK-corr-first"),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(Guid.NewGuid(), "rejection", "{\"expectedVersion\":\"2\"}", "SENTETIK-self"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-corr-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SalesCustomerReturnProblemCodesV1.SelfApproval, body.RootElement.GetProperty("code").GetString());
    }

    private static HttpRequestMessage Post(Guid returnId, string route, string json, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.CustomerReturnsPath}/{returnId:D}/{route}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return request;
    }

    private WebApplicationFactory<Program> CreateHost(ISalesCustomerReturnService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesCustomerReturnService>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private static SalesCustomerReturnView View(Guid id)
    {
        var amounts = new SalesCustomerReturnAmountsView("10.00", "2.00", "0.00", "12.00", "12.00", "0.00");
        var line = new SalesCustomerReturnLineView(1, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1", "10.00", "20",
            null, false, false, amounts, null);
        return new SalesCustomerReturnView(id, "Rejected", "3", Guid.NewGuid(), "SF-2026-000001", null, "IADE-SENTETIK-1",
            new DateOnly(2026, 11, 5), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, null, null, null, amounts, null, false,
            "SENTETIK-preparer", DateTimeOffset.UnixEpoch, "SENTETIK-preparer", DateTimeOffset.UnixEpoch, null, null, null, null,
            "SENTETIK-approver", DateTimeOffset.UnixEpoch, "Belge eksik", null, null, null, DateTimeOffset.UnixEpoch, [line]);
    }

    private sealed class StubCustomerReturnService : ISalesCustomerReturnService
    {
        public Func<string, Guid, Task<SalesCustomerReturnCommandResult>> Decide { get; set; } =
            (_, _) => throw new InvalidOperationException("The service must not be called.");
        public List<string> Bodies { get; } = [];

        public Task<SalesCustomerReturnCommandResult> RejectCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Record("reject", customerReturnId, request);

        public Task<SalesCustomerReturnCommandResult> CancelCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Record("cancel", customerReturnId, request);

        public Task<SalesCustomerReturnCommandResult> SubmitCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Record("submit", customerReturnId, request);

        public Task<SalesCustomerReturnCommandResult> ApproveCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Record("approve", customerReturnId, request);

        public Task<SalesCustomerReturnCommandResult> FinalizeCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Record("finalize", customerReturnId, request);

        private Task<SalesCustomerReturnCommandResult> Record(string command, Guid customerReturnId, SalesDraftStateCommandRequest request)
        {
            Bodies.Add($"{command}:{customerReturnId:D}:{request.ExpectedVersion}:{request.Reason}");
            return Decide(command, customerReturnId);
        }

        public Task<SalesCustomerReturnCommandResult> CreateCustomerReturnAsync(CreateSalesCustomerReturnRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not called");

        public Task<SalesCustomerReturnView> GetCustomerReturnAsync(Guid customerReturnId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not called");

        public Task<SalesCustomerReturnListResponse> ListCustomerReturnsAsync(Guid? originalInvoiceId,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not called");
    }
}
