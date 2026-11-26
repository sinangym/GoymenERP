using System.Net;
using System.Net.Http.Json;
using System.Text;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real HTTP/middleware, synthetic command owner. SQL atomicity is tested separately.</summary>
public sealed class WorkDelegationEndpointTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/work/delegations";
    private static readonly Guid Target = Guid.Parse("d3130000-0000-4000-8000-000000000001");
    private static readonly CreateWorkDelegationV1 Input = new("sales.order.v1", Target,
        new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), "SENTETIK absence");

    [Fact]
    public async Task Anonymous_and_foreign_company_requests_stop_before_the_command_owner()
    {
        var service = new Owner(); using var host = Host(service);
        using var anonymous = host.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        using var denied = await anonymous.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var foreign = Client(host);
        foreign.DefaultRequestHeaders.Remove("X-Company-Id");
        foreign.DefaultRequestHeaders.Add("X-Company-Id", Guid.NewGuid().ToString("D"));
        using var forbidden = await foreign.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Strict_command_body_and_metadata_reject_unknown_company_fields_missing_keys_and_wrong_version_types()
    {
        var service = new Owner(); using var host = Host(service); using var client = Client(host);
        using var missing = await client.PostAsJsonAsync(Path, Input);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "SENTETIK-key");
        foreach (var body in new[] { "{\"companyId\":\"" + TestCompanyIds.CompanyB + "\"}", "{\"kind\":", "null" })
        {
            using var response = await client.PostAsync(Path, new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var numericVersion = await client.PostAsync($"{Path}/{Target:D}/revocation",
            new StringContent("{\"expectedVersion\":1,\"reason\":\"SENTETIK\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, numericVersion.StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "one,two");
        using var multiple = await client.PostAsJsonAsync(Path, Input);
        Assert.Equal(HttpStatusCode.BadRequest, multiple.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Create_revoke_and_list_keep_the_owner_contract_pagination_no_store_and_original_replay_correlation()
    {
        var service = new Owner(); using var host = Host(service); using var client = Client(host);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "SENTETIK-key");
        using var created = await client.PostAsJsonAsync(Path, Input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(Input, service.Input);
        Assert.Equal("SENTETIK-key", service.Metadata!.IdempotencyKey);
        Assert.Equal("SENTETIK-http", service.Metadata.CorrelationId);
        Assert.Equal("true", Assert.Single(created.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-original", Assert.Single(created.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.True(created.Headers.CacheControl!.Private); Assert.True(created.Headers.CacheControl.NoStore);
        using var revoked = await client.PostAsJsonAsync($"{Path}/{Target:D}/revocation", new RevokeWorkDelegationV1("1", "SENTETIK return"));
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal(Target, service.RevokedId);
        using var listed = await client.GetAsync(Path + "?all=true&page=2");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal((true, 2), service.Page);
        Assert.True(listed.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Terminal_conflict_replays_original_headers_and_feature_unavailability_stays_code_less()
    {
        var service = new Owner { Failure = new(409, WorkDelegationCodesV1.Chain, true, "SENTETIK-first") };
        using var host = Host(service); using var client = Client(host);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "SENTETIK-key");
        using var conflict = await client.PostAsJsonAsync(Path, Input);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("SENTETIK-first", Assert.Single(conflict.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Contains(WorkDelegationCodesV1.Chain, await conflict.Content.ReadAsStringAsync());
        service.Failure = new(503, WorkDelegationCodesV1.Disabled);
        using var unavailable = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain(WorkDelegationCodesV1.Disabled, await unavailable.Content.ReadAsStringAsync());
    }

    private WebApplicationFactory<Program> Host(Owner service) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IWorkDelegationServiceV1>(); services.AddSingleton<IWorkDelegationServiceV1>(service);
    }));
    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "SENTETIK-http");
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }
    private sealed class Owner : IWorkDelegationServiceV1
    {
        public int Calls { get; private set; }
        public CreateWorkDelegationV1? Input { get; private set; }
        public WorkCommandMetadataV1? Metadata { get; private set; }
        public Guid RevokedId { get; private set; }
        public (bool All, int Page) Page { get; private set; }
        public WorkDelegationException? Failure { get; set; }
        private void Enter() { Calls++; if (Failure is { } failure) throw failure; }
        public Task<WorkDelegationTableV1> ListAsync(bool all = false, int page = 0, CancellationToken cancellationToken = default)
        { Enter(); Page = (all, page); return Task.FromResult(new WorkDelegationTableV1(TestCompanyIds.CompanyA, [], page, false)); }
        public Task<WorkDelegationCommandResultV1> CreateAsync(CreateWorkDelegationV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
        { Enter(); Input = request; Metadata = metadata; return Task.FromResult(Result()); }
        public Task<WorkDelegationCommandResultV1> RevokeAsync(Guid id, RevokeWorkDelegationV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
        { Enter(); RevokedId = id; return Task.FromResult(Result()); }
        private static WorkDelegationCommandResultV1 Result() => new(new(Target, TestCompanyIds.CompanyA, "sales.order.v1", "sales.orders.approve",
            Guid.NewGuid(), Target, InputDate, InputDate.AddDays(1), "1", Guid.NewGuid(), InputDate, "SENTETIK", null, null, null), true, "SENTETIK-original");
        private static readonly DateTimeOffset InputDate = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);
    }
}
