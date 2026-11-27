using System.Net;
using System.Text;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.Api.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// Actual host routing/metadata and closed-feature transport checks; not SQL persistence acceptance.
public sealed class AiFeedbackEndpointContractTests
{
    private const string Path = "/api/v1/ai/answers/aaaaaaaa-1000-4000-8000-000000000002/feedback";
    private const string Body = "{\"expectedVersion\":\"0\",\"kind\":\"Accepted\",\"reasonCode\":null}";

    [Theory]
    [InlineData("GetAiAnswerFeedbackV1", false)] [InlineData("RecordAiAnswerFeedbackV1", true)]
    public async Task The_actual_host_maps_company_authenticated_feedback_and_its_declared_failures(string name, bool command)
    {
        await using var factory = new GoymenErpApiFactory(); using var client = factory.CreateClient();
        var route = Assert.Single(factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>(), endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
        Assert.Equal("/api/v1/ai/answers/{answerId:guid}/feedback", route.RoutePattern.RawText);
        Assert.NotEmpty(route.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var statuses = route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(item => item.StatusCode).ToHashSet();
        Assert.Subset(statuses, new HashSet<int> { 200, 400, 401, 403, 404, 503 });
        if (command) Assert.Subset(statuses, new HashSet<int> { 409, 415 });
    }

    [Theory]
    [InlineData("unknown", 400)] [InlineData("missing-key", 400)] [InlineData("multiple-keys", 400)]
    [InlineData("non-json", 415)] [InlineData("null", 400)] [InlineData("missing-field", 400)]
    public async Task Strict_transport_errors_are_refused_before_a_closed_runtime_or_owner(string fault, int expected)
    {
        await using var factory = new GoymenErpApiFactory(); using var client = factory.CreateClient();
        var body = fault == "null" ? "null" : fault == "missing-field" ? "{\"expectedVersion\":\"0\",\"kind\":\"Accepted\"}"
            : fault == "unknown" ? Body.Insert(1, "\"companyId\":\"aaaaaaaa-1000-4000-8000-000000000002\",") : Body;
        using var request = Request(body);
        if (fault == "missing-key") request.Headers.Remove("Idempotency-Key");
        if (fault == "multiple-keys") request.Headers.Add("Idempotency-Key", "SENTETIK-second");
        if (fault == "non-json") request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_runtime_never_reports_a_recorded_observation_or_a_feedback_receipt()
    {
        await using var factory = new GoymenErpApiFactory(); using var client = factory.CreateClient();
        using var request = Request(Body); using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public void A_standalone_runtime_without_the_feedback_owner_preserves_the_unmeasured_source_state()
    {
        var services = new ServiceCollection(); services.AddAiRuntime(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IAiAcceptanceCountSourceV1>());
    }

    private static HttpRequestMessage Request(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Development-User", TestUsers.CompanyA);
        request.Headers.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        request.Headers.Add("Idempotency-Key", "SENTETIK-feedback-http");
        return request;
    }
}
