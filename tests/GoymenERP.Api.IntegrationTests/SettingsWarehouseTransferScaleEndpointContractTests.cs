using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Source-only HTTP boundary cases, synthetic owner responses; no SQL or production activation evidence.</summary>
public sealed class SettingsWarehouseTransferScaleEndpointContractTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Location = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Site = Guid.Parse("44444444-4444-4444-8444-444444444444");
    [Theory]
    [InlineData("GetSettingsWarehouseTransferScaleCandidatesV1", "/warehouses", false)]
    [InlineData("GetSettingsWarehouseTransferScaleV1", "/{locationId:guid}", false)]
    [InlineData("RecordSettingsWarehouseTransferScaleV1", "/{locationId:guid}/versions", true)]
    public async Task Warehouse_scale_routes_require_the_existing_explicit_management_permission(string name, string suffix, bool command)
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health/live");
        health.EnsureSuccessStatusCode();
        var route = factory.Services.GetServices<EndpointDataSource>().SelectMany(x => x.Endpoints).OfType<RouteEndpoint>()
            .Single(x => x.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
        Assert.Equal(SettingsWarehouseTransferScaleEndpoints.BasePath + suffix, route.RoutePattern.RawText);
        Assert.NotNull(route.Metadata.GetMetadata<RequiresCompanyContextAttribute>());
        Assert.NotNull(route.Metadata.GetMetadata<PrivateNoStoreOnAuthorizationFailureMetadata>());
        Assert.Contains(route.Metadata.GetOrderedMetadata<IAuthorizeData>(), x => x.Policy == SettingsWarehouseTransferScaleV1.Manage);
        Assert.DoesNotContain(route.Metadata.GetOrderedMetadata<IAuthorizeData>(), x => x.Policy == ErpPermissions.OrganizationLocationsRead);
        Assert.Equal(command, route.Metadata.Any(x => x.GetType().Name == "IdempotentCommandMetadata"));
        var statuses = route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(x => x.StatusCode).ToHashSet();
        Assert.Subset(statuses, new HashSet<int> { 200, 400, 401, 403, 503 }.Where(x => !command || x != 200).ToHashSet());
        if (command) Assert.Contains(204, statuses);
    }
    [Theory]
    [InlineData("", null, 25)] [InlineData("?q=%20DEPO-01%20&limit=100", "DEPO-01", 100)]
    public void Candidate_query_preserves_actual_warehouse_text_and_the_owner_window(string raw, string? q, int limit)
    {
        var context = Context(raw);
        Assert.True(SettingsWarehouseTransferScaleEndpoints.TryQuery(context.Request.Query, out var query));
        Assert.Equal((q, limit), (query.Q, query.Limit));
    }
    [Theory]
    [InlineData("?q=a&q=b")] [InlineData("?limit=25&limit=25")] [InlineData("?companyId=other")]
    [InlineData("?limit=0")] [InlineData("?limit=101")] [InlineData("?limit=01")]
    [InlineData("?limit=+1")] [InlineData("?q=a%0Ab")]
    public void Candidate_query_refuses_duplicate_foreign_scope_and_invalid_values(string raw) =>
        Assert.False(SettingsWarehouseTransferScaleEndpoints.TryQuery(Context(raw).Request.Query, out _));
    [Fact]
    public async Task Candidates_use_real_owner_identity_under_only_the_settings_management_fence()
    {
        var company = new CompanyContext(Company); var user = new User(); var authority = new Authority();
        var source = new Candidates((actual, _) =>
        {
            Assert.Equal(Company, actual);
            return Task.FromResult(new OrganizationLocationCandidatesV1([new(Location, "DEP-01", "SENTETIK depo", Site, "TES-01", "SENTETIK tesis")], false));
        });
        var context = Context("?q=DEP-01&limit=25");
        var result = await SettingsWarehouseTransferScaleEndpoints.CandidatesAsync(context, company, user, authority, source, default);
        var body = Assert.IsType<WarehouseTransferScaleCandidatesResponseV1>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Equal((Company, "DEP-01", 25), (body.CompanyId, body.Q, body.Limit));
        Assert.Equal(Location, Assert.Single(body.Items).LocationId);
        Assert.Equal(new[] { SettingsWarehouseTransferScaleV1.Manage, SettingsWarehouseTransferScaleV1.Manage }, authority.Asked);
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl.ToString());
    }
    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("permission")]
    public async Task A_candidate_answer_cannot_cross_a_company_actor_or_authority_change(string drift)
    {
        var company = new CompanyContext(Company); var user = new User(); var authority = new Authority();
        var source = new Candidates((_, _) =>
        {
            if (drift == "company") company.Set(Other);
            if (drift == "actor") user.UserId = "SENTETIK-other";
            if (drift == "permission") authority.Granted = false;
            return Task.FromResult(new OrganizationLocationCandidatesV1([], false));
        });
        var result = await SettingsWarehouseTransferScaleEndpoints.CandidatesAsync(Context(), company, user, authority, source, default);
        Assert.Equal(403, Status(result));
    }
    [Fact]
    public async Task An_unreadable_owner_is_not_an_empty_candidate_list_and_cancellation_propagates()
    {
        var source = new Candidates((_, _) => throw new InvalidOperationException("Synthetic owner fault."));
        Assert.Equal(503, Status(await SettingsWarehouseTransferScaleEndpoints.CandidatesAsync(Context(), new CompanyContext(Company), new User(), new Authority(), source, default)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SettingsWarehouseTransferScaleEndpoints.CandidatesAsync(Context(), new CompanyContext(Company), new User(), new Authority(), source, cancellation.Token));
    }
    [Fact]
    public async Task Policy_reads_refuse_a_foreign_nested_owner_frame_instead_of_releasing_it()
    {
        var service = new Service { Read = _ => Task.FromResult(View() with { Policy = View().Policy with { CompanyId = Other } }) };
        Assert.Equal(503, Status(await SettingsWarehouseTransferScaleEndpoints.ReadAsync(Location, Context(), new CompanyContext(Company), new User(), service, default)));
    }
    [Theory]
    [InlineData(204, null, false)] [InlineData(204, null, true)]
    [InlineData(409, SettingsWarehouseTransferScaleEndpoints.VersionConflict, true)]
    public async Task Commands_preserve_the_owner_terminal_and_its_original_replay_correlation(int status, string? code, bool replay)
    {
        var service = new Service { Record = (_, metadata) =>
        {
            Assert.Equal((Company, "SENTETIK-actor", "one-key"), (metadata.CompanyId, metadata.ActorId, metadata.IdempotencyKey));
            return Task.FromResult(new CompanyPolicyCommandResultV1(new(status, code, "SENTETIK", "SENTETIK"), replay, "synthetic-original"));
        } };
        var context = Context(); context.Request.Headers["Idempotency-Key"] = " one-key ";
        var result = await SettingsWarehouseTransferScaleEndpoints.RecordAsync(Location, new("0", false, "SENTETIK reason"), context,
            new CompanyContext(Company), new User(), new Correlation(), service, default);
        Assert.Equal(status, Status(result));
        Assert.Equal(replay ? "true" : "", context.Response.Headers["Idempotency-Replayed"].ToString());
        Assert.Equal(replay ? "synthetic-original" : "", context.Response.Headers["Idempotency-Original-Correlation-Id"].ToString());
        if (status == 409) Assert.Equal(code, Assert.IsType<ProblemDetails>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value).Extensions["code"]);
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("a,b")] [InlineData("a\nb")]
    public async Task Invalid_command_identity_never_reaches_the_owner(string? key)
    {
        var service = new Service { Record = (_, _) => throw new InvalidOperationException("Owner must not be called.") };
        var context = Context(); if (key is not null) context.Request.Headers["Idempotency-Key"] = key;
        var result = await SettingsWarehouseTransferScaleEndpoints.RecordAsync(Location, new("0", false, "SENTETIK"), context,
            new CompanyContext(Company), new User(), new Correlation(), service, default);
        Assert.Equal(400, Status(result));
    }
    [Fact]
    public void The_command_does_not_admit_a_client_company_or_unknown_fields() => Assert.Throws<JsonException>(() =>
        JsonSerializer.Deserialize<RecordWarehouseTransferScaleV1>("{\"expectedVersion\":\"0\",\"requireExitWeighing\":false,\"reason\":\"SENTETIK\",\"companyId\":\"foreign\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    private static DefaultHttpContext Context(string query = "") { var context = new DefaultHttpContext(); context.Request.QueryString = new(query); return context; }
    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
    private static WarehouseTransferScaleViewV1 View() => new(Company, Location,
        new(SettingsWarehouseTransferScaleStatusV1.NotConfigured, Company, Location, false, null, SettingsWarehouseTransferScaleV1.SourceVersion + "/not-configured"), []);
    private sealed class CompanyContext(Guid id) : ICompanyContext { public bool IsResolved => true; public Guid CompanyId { get; private set; } = id; public void Set(Guid value) => CompanyId = value; }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId { get; set; } = "SENTETIK-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company, Other };
        public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true; public List<string> Asked { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken ct = default)
        { Asked.Add(permission); return Task.FromResult(new PermissionScope(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Candidates(Func<Guid, OrganizationLocationCandidateQueryV1, Task<OrganizationLocationCandidatesV1>> read) : IOrganizationLocationCandidatesV1
    { public Task<OrganizationLocationCandidatesV1> SearchAsync(Guid companyId, OrganizationLocationCandidateQueryV1 query, CancellationToken ct = default) => read(companyId, query); }
    private sealed class Service : ISettingsWarehouseTransferScaleAdministrationV1
    {
        public Func<Guid, Task<WarehouseTransferScaleViewV1>> Read { get; init; } = _ => Task.FromResult(View());
        public Func<RecordWarehouseTransferScaleV1, SettingsCommandMetadataV1, Task<CompanyPolicyCommandResultV1>> Record { get; init; } = (_, _) => throw new InvalidOperationException();
        public Task<WarehouseTransferScaleViewV1> ReadAsync(Guid locationId, CancellationToken ct = default) => Read(locationId);
        public Task<CompanyPolicyCommandResultV1> RecordAsync(Guid locationId, RecordWarehouseTransferScaleV1 request, SettingsCommandMetadataV1 metadata, CancellationToken ct = default) => Record(request, metadata);
    }
    private sealed class Correlation : ICorrelationContext
    {
        public bool IsResolved => true; public string CorrelationId => "synthetic-current"; public string? CausationId => null;
        public string SourceChannel => "Web"; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) => throw new InvalidOperationException();
    }
}
