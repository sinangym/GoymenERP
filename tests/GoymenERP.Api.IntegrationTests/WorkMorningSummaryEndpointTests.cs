using System.Net;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Work.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Existing real HTTP/auth/company host with a controlled read port. Not scheduler, owner SQL or AI acceptance.</summary>
public sealed class WorkMorningSummaryEndpointTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/work/morning-summary";
    private static readonly Guid Digest = Guid.Parse("d7900000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Slot = DateTimeOffset.Parse("2026-11-27T06:00:00Z");

    [Fact]
    public async Task Summary_200_has_only_the_five_owner_fields_and_actual_counts_with_private_no_store()
    {
        var owner = new Owner { Value = new(TestCompanyIds.CompanyA, Digest, Slot, "settings-v1", [new("task-assigned", 3)]) };
        using var host = Host(owner); using var client = Client(host);
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        NoStore(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["companyId", "digestId", "slotUtc", "scheduleVersion", "counts"],
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(TestCompanyIds.CompanyA, json.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal(Digest, json.RootElement.GetProperty("digestId").GetGuid());
        Assert.Equal(Slot, json.RootElement.GetProperty("slotUtc").GetDateTimeOffset());
        Assert.Equal("settings-v1", json.RootElement.GetProperty("scheduleVersion").GetString());
        var count = Assert.Single(json.RootElement.GetProperty("counts").EnumerateArray());
        Assert.Equal(["kind", "count"], count.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("task-assigned", count.GetProperty("kind").GetString());
        Assert.Equal(3, count.GetProperty("count").GetInt32());
        Assert.Equal(1, owner.Calls);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task No_released_digest_returns_empty_204_not_a_fabricated_zero_summary()
    {
        var owner = new Owner(); using var host = Host(owner); using var client = Client(host);
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        NoStore(response);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, owner.Calls);
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(503)]
    public async Task Owner_refusal_remains_code_less_and_never_discloses_a_partial_summary(int status)
    {
        var owner = new Owner { Status = status }; using var host = Host(owner); using var client = Client(host);
        using var response = await client.GetAsync(Path);
        Assert.Equal(status, (int)response.StatusCode);
        NoStore(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("code", out _));
        Assert.False(json.RootElement.TryGetProperty("counts", out _));
        Assert.False(json.RootElement.TryGetProperty("digestId", out _));
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task Anonymous_and_foreign_company_requests_are_refused_before_the_read_port()
    {
        var owner = new Owner(); using var host = Host(owner);
        using var anonymous = host.CreateClient();
        using var unauthorized = await anonymous.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var client = Client(host);
        client.DefaultRequestHeaders.Remove("X-Company-Id");
        client.DefaultRequestHeaders.Add("X-Company-Id", Guid.NewGuid().ToString("D"));
        using var forbidden = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Query_values_cannot_choose_a_company_recipient_or_model_for_the_argument_free_owner_read()
    {
        var owner = new Owner(); using var host = Host(owner); using var client = Client(host);
        using var response = await client.GetAsync(Path + "?companyId=" + Guid.NewGuid() + "&recipient=" + Guid.NewGuid() + "&model=SENTETIK");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(TestCompanyIds.CompanyA, owner.CompanyId);
        Assert.False(string.IsNullOrWhiteSpace(owner.ActorId));
        Assert.Equal(1, owner.Calls);
    }

    [Fact]
    public void Normal_host_maps_the_authenticated_company_read_and_exact_success_failure_metadata()
    {
        using var host = Host(new Owner()); using var client = Client(host);
        var route = Assert.Single(host.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>(), endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetMyWorkMorningSummaryV1");
        Assert.Equal(Path, route.RoutePattern.RawText);
        Assert.Equal(["GET"], route.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.NotEmpty(route.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.NotNull(route.Metadata.GetMetadata<RequiresCompanyContextAttribute>());
        var responses = route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Equal(typeof(WorkMorningSummaryV1), Assert.Single(responses, response => response.StatusCode == 200).Type);
        Assert.Contains(responses, response => response.StatusCode == 204);
        foreach (var status in new[] { 401, 403, 503 }) Assert.Contains(responses, response => response.StatusCode == status);
        Assert.DoesNotContain(responses, response => response.StatusCode == 201);
    }

    private WebApplicationFactory<Program> Host(Owner owner) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IWorkMorningSummaryReadV1>();
        services.AddScoped<IWorkMorningSummaryReadV1>(provider => new ScopedOwner(owner,
            provider.GetRequiredService<ICompanyContext>(), provider.GetRequiredService<ICurrentUser>()));
    }));

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static void NoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
        Assert.Contains(response.Headers.Pragma, value => value.Name == "no-cache");
    }

    private sealed class Owner
    {
        public WorkMorningSummaryV1? Value { get; init; }
        public int? Status { get; init; }
        public int Calls { get; set; }
        public Guid CompanyId { get; set; }
        public string? ActorId { get; set; }
    }

    private sealed class ScopedOwner(Owner owner, ICompanyContext company, ICurrentUser user) : IWorkMorningSummaryReadV1
    {
        public Task<WorkMorningSummaryV1?> ReadAsync(CancellationToken cancellationToken = default)
        {
            owner.Calls++; owner.CompanyId = company.CompanyId; owner.ActorId = user.UserId;
            if (owner.Status is { } status) throw new WorkCollaborationException(status, "SENTETIK-private-code");
            return Task.FromResult(owner.Value);
        }
    }
}
