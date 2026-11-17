using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

// HTTP boundary tests use a named synthetic host; the real owner/transaction tests are separate SQL facts.
public sealed class LogisticsSubcontractDeliveryHttpContractTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    [Theory]
    [InlineData("{\"instructionId\":\"00000000-0000-0000-0000-000000000001\",\"instructionId\":\"00000000-0000-0000-0000-000000000002\",\"effectiveOn\":\"2026-11-17\",\"lines\":[]}")]
    [InlineData("{\"instructionId\":\"00000000-0000-0000-0000-000000000001\",\"effectiveOn\":\"2026-11-17\",\"lines\":[],\"companyId\":\"00000000-0000-0000-0000-000000000002\"}")]
    [InlineData("{\"instructionId\":\"bad\",\"effectiveOn\":\"2026-11-17\",\"lines\":[]}")]
    public async Task Duplicate_unknown_or_malformed_members_are_refused_before_the_subcontract_host(string json)
    {
        var stub = new SyntheticHost(); using var host = Host(stub); using var client = Client(host);
        using var response = await client.SendAsync(Post("", json)); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, stub.Calls);
    }

    [Theory]
    [InlineData("?pageIndex=00")]
    [InlineData("?pageIndex=-1")]
    [InlineData("?pageIndex=0&pageIndex=1")]
    [InlineData("?companyId=foreign")]
    public async Task Candidate_queries_reject_loose_paging_or_client_company_before_the_owner(string query)
    {
        var stub = new SyntheticHost(); using var host = Host(stub); using var client = Client(host);
        using var response = await client.GetAsync($"{LogisticsSubcontractDeliveryV1.Path}/candidates/{Guid.NewGuid():D}/{Guid.NewGuid():D}{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task Missing_subcontract_composition_is_an_explicit_safe_503_and_never_a_fake_empty_list()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.RemoveAll<ILogisticsSubcontractDeliveryHostV1>()));
        using var client = Client(host); using var response = await client.GetAsync(LogisticsSubcontractDeliveryV1.Path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("service_unavailable", body.GetProperty("code").GetString()); Assert.False(body.TryGetProperty("items", out _));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(503, "SENTETIK-private-database-message")]
    [InlineData(409, LogisticsSubcontractDeliveryV1.ActiveInvoice)]
    public async Task A_claim_refusal_replays_only_the_public_terminal_code_and_a_technical_outage_hides_private_details(int status, string code)
    {
        var stub = new SyntheticHost { Failure = new(status, code, isReplay: status == 409, originalCorrelationId: "SENTETIK-original") };
        using var host = Host(stub); using var client = Client(host);
        using var response = await client.SendAsync(Post($"/{Guid.NewGuid():D}/weighing-corrections", JsonSerializer.Serialize(new
        { expectedVersion = "7", weighingId = Guid.NewGuid(), expectedWeighingRevision = 1, correctionId = Guid.NewGuid(), direction = "TareFirst", firstWeighingKg = "100", secondWeighingKg = "106", reason = "SENTETIK gerçek gerekçe" })));
        Assert.Equal(status, (int)response.StatusCode); var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(status == 503 ? "service_unavailable" : code, JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
        if (status == 503) { Assert.DoesNotContain(code, text); Assert.False(response.Headers.Contains("Idempotency-Replayed")); }
        else { Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed"))); Assert.Equal("SENTETIK-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id"))); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_trusted_original_or_replayed_physical_refusal_preserves_the_original_available_and_requested_kg(bool replay)
    {
        const string detail = "Sistemde 20 kg var, 80 kg istendi.";
        var stub = new SyntheticHost { Failure = new(409, "inventory_physical_stock_insufficient", isReplay: replay,
            originalCorrelationId: replay ? "SENTETIK-original-physical" : null, detail: detail) };
        using var host = Host(stub); using var client = Client(host);
        using var response = await client.SendAsync(Post($"/{Guid.NewGuid():D}/weighing-corrections", CorrectionBody()));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(detail, body.RootElement.GetProperty("detail").GetString());
        Assert.Equal("inventory_physical_stock_insufficient", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("stockShortfall", out _)); // Legacy stored detail has no invented structured IDs.
        Assert.Equal(replay, response.Headers.Contains("Idempotency-Replayed"));
        if (replay) Assert.Equal("SENTETIK-original-physical", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    [Theory]
    [InlineData(401, "inventory_physical_stock_insufficient", "Sistemde 20 kg var, 80 kg istendi.")]
    [InlineData(403, "inventory_physical_stock_insufficient", "Sistemde 20 kg var, 80 kg istendi.")]
    [InlineData(503, "inventory_physical_stock_insufficient", "Sistemde 20 kg var, 80 kg istendi.")]
    [InlineData(409, LogisticsSubcontractDeliveryV1.ActiveInvoice, "Sistemde 20 kg var, 80 kg istendi.")]
    [InlineData(409, "inventory_physical_stock_insufficient", "SENTETIK arbitrary exception text")]
    public async Task Nondisclosable_or_nonphysical_or_untrusted_detail_is_never_returned_as_stock_kg(int status, string code, string detail)
    {
        var stub = new SyntheticHost { Failure = new(status, code, detail: detail) };
        using var host = Host(stub); using var client = Client(host);
        using var response = await client.SendAsync(Post($"/{Guid.NewGuid():D}/weighing-corrections", CorrectionBody()));
        Assert.Equal(status, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(detail, text);
        using var body = JsonDocument.Parse(text); Assert.False(body.RootElement.TryGetProperty("stockShortfall", out _));
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    private static string CorrectionBody() => JsonSerializer.Serialize(new
    {
        expectedVersion = "7", weighingId = Guid.NewGuid(), expectedWeighingRevision = 1, correctionId = Guid.NewGuid(),
        direction = "TareFirst", firstWeighingKg = "100", secondWeighingKg = "106", reason = "SENTETIK gerçek gerekçe"
    });

    private WebApplicationFactory<Program> Host(SyntheticHost stub) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<ILogisticsSubcontractDeliveryHostV1>(_ => stub))));
    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Development-User", "company-a-user"); client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D")); return client;
    }
    private static HttpRequestMessage Post(string suffix, string json)
    { var request = new HttpRequestMessage(HttpMethod.Post, LogisticsSubcontractDeliveryV1.Path + suffix) { Content = new StringContent(json, Encoding.UTF8, "application/json") }; request.Headers.Add("Idempotency-Key", "SENTETIK-s3-wire"); return request; }
    private sealed class SyntheticHost : ILogisticsSubcontractDeliveryHostV1
    {
        public int Calls { get; private set; } public LogisticsInstructionServiceException? Failure { get; init; }
        private T Answer<T>() { Calls++; throw (Exception?)Failure ?? new InvalidOperationException("The boundary should have refused the request."); }
        public Task<LogisticsSubcontractListV1> ListAsync(string? cursor, CancellationToken token = default) => Answer<Task<LogisticsSubcontractListV1>>();
        public Task<LogisticsSubcontractSalesLinesV1> SalesLinesAsync(string? cursor, CancellationToken token = default) => Answer<Task<LogisticsSubcontractSalesLinesV1>>();
        public Task<LogisticsSubcontractCandidatesV1> CandidatesAsync(Guid order, Guid line, int page, CancellationToken token = default) => Answer<Task<LogisticsSubcontractCandidatesV1>>();
        public Task<LogisticsSubcontractInstructionV1> ReadAsync(Guid id, CancellationToken token = default) => Answer<Task<LogisticsSubcontractInstructionV1>>();
        public Task<LogisticsSubcontractInstructionV1> CreateAsync(CreateSubcontractDeliveryV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default) => Answer<Task<LogisticsSubcontractInstructionV1>>();
        public Task<LogisticsSubcontractInstructionV1> LoadAsync(Guid id, Guid lineId, RecordLoadRequest request, LogisticsCommandMetadata metadata, CancellationToken token = default) => Answer<Task<LogisticsSubcontractInstructionV1>>();
        public Task<LogisticsSubcontractInstructionV1> ShipAsync(Guid id, ShipSubcontractDeliveryV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default) => Answer<Task<LogisticsSubcontractInstructionV1>>();
        public Task<LogisticsSubcontractInstructionV1> CorrectWeighingAsync(Guid id, CorrectSubcontractWeighingV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default) => Answer<Task<LogisticsSubcontractInstructionV1>>();
    }
}
