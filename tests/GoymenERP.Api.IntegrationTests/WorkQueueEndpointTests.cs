using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkQueueEndpointTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Request = Guid.Parse("c6000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task The_queue_answers_the_exact_json_with_private_no_store()
    {
        var source = new Source("purchasing", "purchasing.request.v1")
        {
            Result = context => NativeApprovalQueueResultV1.Found(context.CompanyId,
                [new("purchasing", Request, "purchasing.request.v1",
                    DateTimeOffset.Parse("2026-11-01T07:00:00Z"), $"/satin-alma/talepler/{Request:D}")],
                hasMore: true),
        };
        using var host = Host([source]);
        using var client = Client(host);

        using var response = await client.GetAsync(WorkQueueEndpoints.Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(1, source.Calls);
        Assert.Equal(Company, source.Context!.CompanyId);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["companyId", "items", "sources"],
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(Company, json.RootElement.GetProperty("companyId").GetGuid());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(["module", "itemId", "kind", "createdAtUtc", "link"],
            item.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(Request, item.GetProperty("itemId").GetGuid());
        Assert.Equal("purchasing.request.v1", item.GetProperty("kind").GetString());
        Assert.Equal($"/satin-alma/talepler/{Request:D}", item.GetProperty("link").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T07:00:00Z"), item.GetProperty("createdAtUtc").GetDateTimeOffset());
        var state = Assert.Single(json.RootElement.GetProperty("sources").EnumerateArray());
        Assert.Equal(["module", "kind", "state", "hasMore"],
            state.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("Ready", state.GetProperty("state").GetString());
        Assert.True(state.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Every_source_denied_is_403_and_every_source_unavailable_is_503()
    {
        var denied = new Source("purchasing", "purchasing.request.v1")
        {
            Result = _ => NativeApprovalQueueResultV1.Denied(),
        };
        var down = new Source("logistics", "logistics.shipment.v1")
        {
            Result = _ => NativeApprovalQueueResultV1.Unavailable(),
        };
        using var deniedHost = Host([denied]);
        using var downHost = Host([down]);
        using var deniedClient = Client(deniedHost);
        using var downClient = Client(downHost);

        using var forbidden = await deniedClient.GetAsync(WorkQueueEndpoints.Path);
        using var unavailable = await downClient.GetAsync(WorkQueueEndpoints.Path);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        AssertPrivateNoStore(forbidden);
        AssertPrivateNoStore(unavailable);
    }

    [Fact]
    public async Task Anonymous_request_is_rejected_before_any_source()
    {
        var source = new Source("purchasing", "purchasing.request.v1");
        using var host = Host([source]);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Company-Id", Company.ToString("D"));

        using var response = await client.GetAsync(WorkQueueEndpoints.Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, source.Calls);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("access")]
    [InlineData("correlation")]
    public async Task Post_await_context_drift_is_denied_over_http(string drift)
    {
        var company = new MutableCompanyContext(Company);
        var user = new MutableUser("checker", Company);
        var correlation = new MutableCorrelation();
        var source = new Source("purchasing", "purchasing.request.v1")
        {
            Result = context =>
            {
                if (drift == "company") company.Set(Guid.NewGuid());
                if (drift == "actor") user.ActorId = "other-checker";
                if (drift == "access") user.AllowedCompanyIds.Remove(Company);
                if (drift == "correlation") correlation.Set("changed-correlation", null, "test", null, null);
                return NativeApprovalQueueResultV1.Found(context.CompanyId, [], hasMore: false);
            },
        };
        using var host = Host([source], services =>
        {
            services.RemoveAll<ICompanyContext>();
            services.AddSingleton<ICompanyContext>(company);
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(user);
            services.RemoveAll<ICorrelationContext>();
            services.AddSingleton<ICorrelationContext>(correlation);
        });
        using var client = Client(host);

        using var response = await client.GetAsync(WorkQueueEndpoints.Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public void Production_registration_resolves_the_queue_over_the_typed_sources()
    {
        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<NativeApprovalQueueReadQueryV1>());
        Assert.All(scope.ServiceProvider.GetServices<INativeApprovalQueueAdapterV1>(), source =>
            Assert.True(NativeApprovalQueueRulesV1.IsValidKind(source.Module, source.Kind)));
    }

    private WebApplicationFactory<Program> Host(
        IReadOnlyList<INativeApprovalQueueAdapterV1> sources,
        Action<IServiceCollection>? configure = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INativeApprovalReadAdapterV1>();
            services.RemoveAll<INativeApprovalQueueAdapterV1>();
            foreach (var source in sources)
            {
                services.AddSingleton(source);
            }
            configure?.Invoke(services);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "b25-queue-endpoint-test");
        client.DefaultRequestHeaders.Add("X-Company-Id", Company.ToString("D"));
        return client;
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
    }

    private sealed class Source(string module, string kind) : INativeApprovalQueueAdapterV1
    {
        public string Module { get; } = module;
        public string Kind { get; } = kind;
        public Func<NativeApprovalQueueContextV1, NativeApprovalQueueResultV1>? Result { get; init; }
        public NativeApprovalQueueContextV1? Context { get; private set; }
        public int Calls { get; private set; }

        public Task<NativeApprovalQueueResultV1> ListPendingAsync(
            NativeApprovalQueueContextV1 context,
            int maxCount,
            CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            return Task.FromResult(Result?.Invoke(context)
                ?? NativeApprovalQueueResultV1.Found(context.CompanyId, [], hasMore: false));
        }
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class MutableUser(string actorId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => ActorId;
        public string ActorId { get; set; } = actorId;
        public HashSet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        IReadOnlySet<Guid> ICurrentUser.AllowedCompanyIds => AllowedCompanyIds;
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class MutableCorrelation : ICorrelationContext
    {
        public bool IsResolved => !string.IsNullOrWhiteSpace(CorrelationId);
        public string CorrelationId { get; private set; } = string.Empty;
        public string? CausationId { get; private set; }
        public string SourceChannel { get; private set; } = string.Empty;
        public string? ClientIpAddress { get; private set; }
        public string? UserAgent { get; private set; }

        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent)
        {
            CorrelationId = correlationId;
            CausationId = causationId;
            SourceChannel = sourceChannel;
            ClientIpAddress = clientIpAddress;
            UserAgent = userAgent;
        }
    }
}
