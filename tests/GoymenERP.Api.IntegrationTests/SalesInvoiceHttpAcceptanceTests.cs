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
/// S5 API-32 (Fatura cekirdegi 1): the invoice routes' HTTP boundary over a SENTETIK <see cref="ISalesInvoiceService"/> (no
/// database, no owner): the metadata and strict body checks that answer before the service, 201 + the absolute Location, the replay
/// headers of the same key again, the 415, the strict list query, the 404 of another company's invoice and the problem shapes
/// (409 with its code and field reasons; 403 / 404 / 503 code-less). The service's own rules are unit-tested; the store is the SQL
/// Server class. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesInvoiceHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public async Task A_created_invoice_answers_201_with_its_absolute_location_and_a_replay_carries_the_replay_headers()
    {
        var invoiceId = Guid.NewGuid();
        var service = new StubInvoiceService
        {
            Create = _ => Task.FromResult(new SalesInvoiceCommandResult(Detail(invoiceId, false), false, "SENTETIK-corr-1")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.SendAsync(Post("SENTETIK-invoice")))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.InvoicesPath.TrimStart('/')}/{invoiceId:D}"), response.Headers.Location);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(invoiceId, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("Draft", body.RootElement.GetProperty("state").GetString());
        }

        service.Create = _ => Task.FromResult(new SalesInvoiceCommandResult(Detail(invoiceId, true), true, "SENTETIK-corr-1"));
        using (var replay = await client.SendAsync(Post("SENTETIK-invoice")))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-corr-1", Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        Assert.Equal(["SENTETIK-invoice", "SENTETIK-invoice"], service.Keys);
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("blank-key")]
    [InlineData("comma-key")]
    [InlineData("not-json")]
    [InlineData("unknown-member")]
    [InlineData("null-share")]
    public async Task The_metadata_and_the_strict_body_answer_before_the_service(string fault)
    {
        var service = new StubInvoiceService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var json = fault switch
        {
            "unknown-member" => "{\"companyAccountId\":\"" + Account + "\",\"price\":\"1\",\"shares\":[{\"shipmentId\":\"" + Guid.NewGuid() + "\"}]}",
            "null-share" => "{\"companyAccountId\":\"" + Account + "\",\"shares\":[null]}",
            _ => JsonSerializer.Serialize(Body(), SalesDraftJson.Options),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.InvoicesPath)
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

    [Fact]
    public async Task A_share_set_that_is_not_ready_is_one_409_with_its_code_and_every_reason_as_a_field_error()
    {
        var service = new StubInvoiceService
        {
            Create = _ => throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.InvoiceNotReady,
                [new("shares[0]", SalesInvoiceReasonCodesV1.ShareAlreadyInvoiced), new("shares[1]", SalesInvoiceReasonCodesV1.RateUnresolved)]),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post("SENTETIK-not-ready"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invoice_not_ready", body.RootElement.GetProperty("code").GetString());
        var errors = body.RootElement.GetProperty("errors").GetRawText();
        Assert.Contains("share_already_invoiced", errors, StringComparison.Ordinal);
        Assert.Contains("rate_unresolved", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(403, "forbidden")]
    [InlineData(503, "permission_unregistered")]
    [InlineData(503, "dispatch_provider_unavailable")]
    public async Task Every_403_and_503_of_the_service_is_code_less_on_the_wire(int status, string code)
    {
        var service = new StubInvoiceService { Create = _ => throw new SalesDraftServiceException(status, code) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post("SENTETIK-refused"));

        Assert.Equal(status, (int)response.StatusCode);
        // The platform writer answers 403 with its own public "forbidden"; the service's setup and provider codes never reach the wire.
        if (code != "forbidden")
        {
            Assert.DoesNotContain(code, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Detail_and_list_answer_private_no_store_another_companys_invoice_is_404_and_the_list_query_is_strict()
    {
        var invoiceId = Guid.NewGuid();
        var service = new StubInvoiceService
        {
            Detail = id => id == invoiceId ? Task.FromResult(Detail(invoiceId, false)) : throw new SalesDraftServiceException(404, "not_found"),
            List = _ => Task.FromResult(new SalesInvoiceListResponse([], null)),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var found = await client.GetAsync($"{SalesEndpoints.InvoicesPath}/{invoiceId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, found.StatusCode);
            Assert.True(found.Headers.CacheControl is { NoStore: true, Private: true });
        }

        using (var other = await client.GetAsync($"{SalesEndpoints.InvoicesPath}/{Guid.NewGuid():D}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        }

        using (var list = await client.GetAsync($"{SalesEndpoints.InvoicesPath}?pageSize=10"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        }

        foreach (var invalid in new[] { "?pageSize=0", "?pageSize=101", "?unknown=1", "?cursor=" })
        {
            using var bad = await client.GetAsync(SalesEndpoints.InvoicesPath + invalid);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
    }

    private WebApplicationFactory<Program> CreateHost(ISalesInvoiceService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesInvoiceService>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private static CreateSalesInvoiceRequest Body() => new(Account, [new SalesInvoiceShareRequest(Guid.Parse("66666666-6666-4666-8666-666666666666"))]);

    private static HttpRequestMessage Post(string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.InvoicesPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(Body(), SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static SalesInvoiceDetailView Detail(Guid id, bool replay)
    {
        var amounts = new SalesInvoiceAmountsView("10.00", "2.00", "0.00", "12.00", "12.00");
        return new SalesInvoiceDetailView(id, "SF-2026-000001", "Draft", "1", new DateOnly(2026, 11, 7), Guid.NewGuid(), Account,
            Guid.NewGuid(), 2, null, amounts, null, [], [], [], "SENTETIK-actor", DateTimeOffset.UnixEpoch, replay);
    }

    private sealed class StubInvoiceService : ISalesInvoiceService
    {
        public Func<CreateSalesInvoiceRequest, Task<SalesInvoiceCommandResult>> Create { get; set; } =
            _ => throw new InvalidOperationException("The service must not be called.");
        public Func<Guid, Task<SalesInvoiceDetailView>> Detail { get; set; } = _ => throw new InvalidOperationException("not called");
        public Func<SalesInvoiceListQuery, Task<SalesInvoiceListResponse>> List { get; set; } = _ => throw new InvalidOperationException("not called");
        public List<string> Keys { get; } = [];

        public Task<SalesInvoiceCommandResult> CreateInvoiceAsync(CreateSalesInvoiceRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(metadata.IdempotencyKey);
            return Create(request);
        }

        public Task<SalesInvoiceListResponse> GetInvoicesAsync(SalesInvoiceListQuery query, CancellationToken cancellationToken = default) =>
            List(query);

        public Task<SalesInvoiceDetailView> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default) => Detail(invoiceId);
    }
}
