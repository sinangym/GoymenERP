using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

// Named synthetic application service: HTTP grammar only, not real owner or SQL evidence.
public sealed class LogisticsPlanExcessHttpContractTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    [Theory]
    [InlineData("{\"expectedVersion\":\"1\",\"expectedVersion\":\"2\",\"expectedInstructionVersion\":\"3\"}")]
    [InlineData("{\"expectedVersion\":\"1\",\"expectedInstructionVersion\":\"3\",\"companyId\":\"foreign\"}")]
    [InlineData("{\"expectedVersion\":\"1\"}")]
    public async Task Duplicate_unknown_or_missing_approval_members_are_rejected_before_the_service(string body)
    {
        var service = new SyntheticPlanExcess(); using var host = Host(service); using var client = Client(host);
        using var response = await client.SendAsync(Post(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, service.Calls);
    }
    [Fact]
    public async Task Recorded_approval_replay_keeps_original_envelope_and_private_no_store_headers()
    {
        var service = new SyntheticPlanExcess { Replay = true }; using var host = Host(service); using var client = Client(host);
        using var response = await client.SendAsync(Post("{\"expectedVersion\":\"1\",\"expectedInstructionVersion\":\"3\"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
    [Fact]
    public async Task An_unavailable_approval_never_discloses_the_exception_text_or_a_false_success()
    {
        var service = new SyntheticPlanExcess { Failure = true }; using var host = Host(service); using var client = Client(host);
        using var response = await client.SendAsync(Post("{\"expectedVersion\":\"1\",\"expectedInstructionVersion\":\"3\"}"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("SENTETIK-private", await response.Content.ReadAsStringAsync()); Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }
    private WebApplicationFactory<Program> Host(SyntheticPlanExcess service) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
        services => services.Replace(ServiceDescriptor.Scoped<ILogisticsPlanExcessServiceV1>(_ => service))));
    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Development-User", "company-a-user");
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D")); return client;
    }
    private static HttpRequestMessage Post(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/logistics/plan-excess/{Guid.NewGuid():D}/approve")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", "SENTETIK-plan-key"); return request;
    }
    private sealed class SyntheticPlanExcess : ILogisticsPlanExcessServiceV1
    {
        public int Calls; public bool Replay; public bool Failure;
        public Task<LogisticsPlanExcessPageV1> ListAsync(string purpose, Guid? instruction, string? cursor, CancellationToken token = default) => Task.FromResult(new LogisticsPlanExcessPageV1([], null));
        public Task<LogisticsPlanExcessResultV1> ProposeAsync(Guid instruction, Guid line, ProposeLogisticsPlanExcessV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default) => throw new NotImplementedException();
        public Task<LogisticsPlanExcessResultV1> ApproveAsync(Guid id, ApproveLogisticsPlanExcessV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default)
        {
            Calls++; if (Failure) throw new InvalidOperationException("SENTETIK-private"); var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new LogisticsPlanExcessResultV1(new(TestCompanyIds.CompanyA, id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "2", "3", "Approved", "100", "20", "0", "0", "SENTETIK", "SENTETIK", now, "SENTETIK", now, "20", "0"), Replay, "SENTETIK-original"));
        }
    }
}
