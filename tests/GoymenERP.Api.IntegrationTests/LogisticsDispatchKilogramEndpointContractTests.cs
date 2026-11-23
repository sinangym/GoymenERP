using System.Net;
using System.Text;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

// HTTP parsing/declared contract only; a spy owner deliberately returns unavailable, never fake persistence success.
public sealed class LogisticsDispatchKilogramEndpointContractTests
{
    [Theory]
    [InlineData("GetLogisticsWeighingDifferenceV1", "/api/v1/logistics/dispatch-notes/{dispatchNoteId:guid}/weighing-difference", false)]
    [InlineData("RecordLogisticsWeighingDifferenceV1", "/api/v1/logistics/weighing-differences", true)]
    [InlineData("ApproveLogisticsWeighingDifferenceV1", "/api/v1/logistics/weighing-differences/{recordId:guid}/approve", true)]
    public async Task Dispatch_kilogram_routes_are_authenticated_and_declare_company_read_and_command_failures(string name, string path, bool command)
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
        var route = factory.Services.GetServices<EndpointDataSource>().SelectMany(x => x.Endpoints).OfType<RouteEndpoint>()
            .Single(x => x.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
        Assert.Equal(path, route.RoutePattern.RawText);
        Assert.NotEmpty(route.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var statuses = route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(x => x.StatusCode).ToHashSet();
        Assert.Subset(statuses, new HashSet<int> { 200, 401, 403, 404, 503 });
        if (command) Assert.Subset(statuses, new HashSet<int> { 400, 409, 415, 422 });
        Assert.Contains(route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(), x => x.StatusCode == 200
            && x.Type == (command ? typeof(DispatchKilogramsResponseWire) : typeof(DispatchKilogramsWire)));
    }

    [Fact]
    public async Task Decimal_text_reaches_the_owner_exactly_without_a_binary_floating_point_conversion()
    {
        var owner = new UnavailableOwner();
        await using var factory = new GoymenErpApiFactory();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<ILogisticsWeighingDifferenceServiceV1>(owner))));
        using var client = app.CreateClient();
        using var command = Request(Body("999999999999999.999"));
        using var response = await client.SendAsync(command);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(999999999999999.999m, Assert.Single(owner.Request!.Products).ScaleKg);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1e3")]
    [InlineData("1.0001")]
    [InlineData("-1")]
    [InlineData("1,25")]
    [InlineData("NaN")]
    public async Task Malformed_kilogram_text_is_rejected_before_calling_the_owner(string kilograms)
    {
        var owner = new UnavailableOwner();
        await using var factory = new GoymenErpApiFactory();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<ILogisticsWeighingDifferenceServiceV1>(owner))));
        using var client = app.CreateClient();
        using var request = Request(Body(kilograms));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(owner.Request);
    }

    [Theory]
    [InlineData("unknown", 400)]
    [InlineData("no-key", 400)]
    [InlineData("no-user", 401)]
    [InlineData("no-company", 400)]
    public async Task Commands_require_strict_body_and_context_metadata_before_the_owner(string fault, int expected)
    {
        var owner = new UnavailableOwner();
        await using var factory = new GoymenErpApiFactory();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<ILogisticsWeighingDifferenceServiceV1>(owner))));
        using var client = app.CreateClient();
        var body = Body("1"); if (fault == "unknown") body = body.Insert(1, "\"companyId\":\"00000000-0000-0000-0000-000000000001\",");
        using var request = Request(body);
        if (fault == "no-key") request.Headers.Remove("Idempotency-Key");
        if (fault == "no-user") request.Headers.Remove("X-Development-User");
        if (fault == "no-company") request.Headers.Remove("X-Company-Id");
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode); Assert.Null(owner.Request);
    }

    private static string Body(string kg) => $$"""{"recordId":"20110000-0000-4000-8000-000000000001","dispatchNoteId":"20110000-0000-4000-8000-000000000002","products":[{"productId":"20110000-0000-4000-8000-000000000003","scaleKg":"{{kg}}"}],"finalizedDocument":null}""";
    private static HttpRequestMessage Request(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/logistics/weighing-differences") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Development-User", TestUsers.CompanyA);
        request.Headers.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString());
        request.Headers.Add("Idempotency-Key", "synthetic-kg-command");
        return request;
    }
    private sealed class UnavailableOwner : ILogisticsWeighingDifferenceServiceV1
    {
        public RecordWeighingDifferenceV1? Request { get; private set; }
        public Task<WeighingDifferenceResponseV1> RecordAsync(RecordWeighingDifferenceV1 request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        { Request = request; return Task.FromException<WeighingDifferenceResponseV1>(new LogisticsWeighingDifferenceServiceException(503, "synthetic_unavailable")); }
        public Task<WeighingDifferenceResponseV1> ApproveAsync(Guid recordId, ApproveWeighingDifferenceV1 request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            Task.FromException<WeighingDifferenceResponseV1>(new LogisticsWeighingDifferenceServiceException(503, "synthetic_unavailable"));
        public Task<WeighingDifferenceViewV1> GetByDispatchNoteAsync(Guid dispatchNoteId, CancellationToken cancellationToken = default) =>
            Task.FromException<WeighingDifferenceViewV1>(new LogisticsWeighingDifferenceServiceException(404, "synthetic_not_found"));
    }
}
