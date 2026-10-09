using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkApprovalEndpointTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Batch = Guid.Parse("a2000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task List_and_detail_publish_the_native_read_contract_as_private_string_enum_json()
    {
        var adapter = new Adapter
        {
            Page = new(ReadOutcome.Found, new NativeApprovalReadPageV1(
                Company,
                [Item()],
                "opaque+/=cursor")),
            Detail = new(ReadOutcome.Found, new NativeApprovalReadDetailV1(Item(), Preview())),
        };
        using var host = Host(adapter);
        using var client = Client(host);

        using var list = await client.GetAsync(WorkApprovalEndpoints.Path + "?cursor=opaque%2B%2F%3Drequest");
        using var detail = await client.GetAsync(
            $"{WorkApprovalEndpoints.Path}/{NativeApprovalKindsV1.FinanceOfficialInvoiceImport}/{Batch:D}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        AssertPrivateNoStore(list);
        Assert.Contains("no-cache", list.Headers.Pragma.Select(value => value.Name));
        Assert.Equal("opaque+/=request", adapter.Cursor);
        Assert.Equal((1, 1), (adapter.ListCalls, adapter.GetCalls));

        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Pending", item.GetProperty("status").GetString());
        Assert.Equal(NativeApprovalKindsV1.FinanceOfficialInvoiceImport, item.GetProperty("kind").GetString());
        Assert.Equal(Company, listJson.RootElement.GetProperty("companyId").GetGuid());

        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(Batch, detailJson.RootElement.GetProperty("item").GetProperty("approvalId").GetGuid());
        Assert.Equal(new string('a', 64), detailJson.RootElement.GetProperty("preview").GetProperty("sourceSha256").GetString());
    }

    [Theory]
    [InlineData(ReadOutcome.Denied, HttpStatusCode.Forbidden)]
    [InlineData(ReadOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ReadOutcome.Unavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Owner_failures_are_not_converted_to_empty_success(
        ReadOutcome outcome,
        HttpStatusCode expectedStatus)
    {
        var adapter = new Adapter { Page = new(outcome, null) };
        using var host = Host(adapter);
        using var client = Client(host);

        using var response = await client.GetAsync(WorkApprovalEndpoints.Path);

        Assert.Equal(expectedStatus, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(1, adapter.ListCalls);
    }

    [Fact]
    public async Task Unknown_kind_is_a_code_less_not_found_without_calling_the_owner()
    {
        var adapter = new Adapter();
        using var host = Host(adapter);
        using var client = Client(host);

        using var response = await client.GetAsync($"{WorkApprovalEndpoints.Path}/unknown/{Batch:D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, adapter.GetCalls);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(problem.RootElement.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Anonymous_request_is_rejected_before_the_native_reader()
    {
        var adapter = new Adapter();
        using var host = Host(adapter);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Company-Id", Company.ToString("D"));

        using var response = await client.GetAsync(WorkApprovalEndpoints.Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, adapter.ListCalls);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("access")]
    [InlineData("correlation")]
    [InlineData("permission")]
    public async Task Post_await_context_or_permission_drift_is_denied_over_http(string drift)
    {
        var company = new MutableCompanyContext(Company);
        var user = new MutableUser("checker", Company);
        var correlation = new MutableCorrelation();
        var adapter = new Adapter
        {
            OnList = context =>
            {
                if (drift == "company") company.Set(Guid.NewGuid());
                if (drift == "actor") user.ActorId = "other-checker";
                if (drift == "access") user.AllowedCompanyIds.Remove(Company);
                if (drift == "correlation") correlation.Set("changed-correlation", null, "test", null, null);
                return drift == "permission"
                    ? new(ReadOutcome.Denied, null)
                    : new(ReadOutcome.Found, new NativeApprovalReadPageV1(context.CompanyId, [], null));
            },
        };
        using var host = Host(adapter, services =>
        {
            services.RemoveAll<ICompanyContext>();
            services.AddSingleton<ICompanyContext>(company);
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(user);
            services.RemoveAll<ICorrelationContext>();
            services.AddSingleton<ICorrelationContext>(correlation);
        });
        using var client = Client(host);

        using var response = await client.GetAsync(WorkApprovalEndpoints.Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, adapter.ListCalls);
    }

    [Fact]
    public async Task Foreign_or_malformed_owner_payloads_fail_closed_over_http()
    {
        var owner = new Owner
        {
            Page = new(ReadOutcome.Found,
                new FinanceOfficialInvoiceImportPendingBatchPageV1(Guid.NewGuid(), [], null)),
            Detail = new(ReadOutcome.Found, null),
        };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1>();
            services.AddSingleton<IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1>(owner);
        }));
        using var client = Client(host);

        using var list = await client.GetAsync(WorkApprovalEndpoints.Path);
        using var detail = await client.GetAsync(
            $"{WorkApprovalEndpoints.Path}/{NativeApprovalKindsV1.FinanceOfficialInvoiceImport}/{Batch:D}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, list.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, detail.StatusCode);
        Assert.Equal((1, 1), (owner.ListCalls, owner.GetCalls));
    }

    [Fact]
    public void Production_registration_resolves_the_read_only_finance_adapter_and_query()
    {
        using var scope = factory.Services.CreateScope();

        var adapters = scope.ServiceProvider.GetServices<INativeApprovalReadAdapterV1>().ToArray();
        Assert.Single(adapters);
        Assert.IsType<FinanceInvoiceImportPendingApprovalAdapterV1>(adapters[0]);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<NativeApprovalReadQueryV1>());
    }

    private WebApplicationFactory<Program> Host(
        Adapter adapter,
        Action<IServiceCollection>? configure = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INativeApprovalReadAdapterV1>();
            services.AddSingleton<INativeApprovalReadAdapterV1>(adapter);
            configure?.Invoke(services);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "d05-endpoint-test");
        client.DefaultRequestHeaders.Add("X-Company-Id", Company.ToString("D"));
        return client;
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
    }

    private static NativeApprovalReadItemV1 Item() => new(
        NativeApprovalKindsV1.FinanceOfficialInvoiceImport,
        Batch,
        Company,
        FinanceOfficialInvoiceImportApprovalStatusV1.Pending,
        "1",
        "source-version-7",
        new string('a', 64),
        FinanceOfficialInvoiceImportTemplateV1.Version,
        1,
        "maker",
        DateTimeOffset.Parse("2026-10-05T07:00:00Z"));

    private static FinanceOfficialInvoiceImportPreviewV1 Preview() => new(
        FinanceOfficialInvoiceImportTemplateV1.Version,
        new string('a', 64),
        new ApprovedFinanceOpenItemSourceBatch(
            Batch,
            Company,
            "source-version-7",
            new string('a', 64),
            [
                new ApprovedFinanceOpenItemSourceRow(
                    Guid.Parse("b1000000-0000-0000-0000-000000000001"),
                    Guid.Parse("b2000000-0000-0000-0000-000000000001"),
                    Guid.Parse("b3000000-0000-0000-0000-000000000001"),
                    "invoice-1",
                    FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                    Guid.Parse("b4000000-0000-0000-0000-000000000001"),
                    Guid.Parse("b5000000-0000-0000-0000-000000000001"),
                    Guid.Parse("b6000000-0000-0000-0000-000000000001"),
                    "1250.50",
                    new DateOnly(2026, 10, 31)),
            ]),
        []);

    private sealed class Adapter : INativeApprovalReadAdapterV1
    {
        public Func<FinanceContext, ReadResult<NativeApprovalReadPageV1>>? OnList { get; init; }
        public ReadResult<NativeApprovalReadPageV1> Page { get; init; } =
            new(ReadOutcome.Found, new NativeApprovalReadPageV1(Company, [], null));
        public ReadResult<NativeApprovalReadDetailV1> Detail { get; init; } =
            new(ReadOutcome.NotFound, null);
        public string? Cursor { get; private set; }
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task<ReadResult<NativeApprovalReadPageV1>> ListAsync(
            FinanceContext context,
            string? cursor,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            Cursor = cursor;
            return Task.FromResult(OnList?.Invoke(context) ?? Page);
        }

        public Task<ReadResult<NativeApprovalReadDetailV1>> GetAsync(
            FinanceContext context,
            string kind,
            Guid approvalId,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(Detail);
        }
    }

    private sealed class Owner : IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1
    {
        public ReadResult<FinanceOfficialInvoiceImportPendingBatchPageV1> Page { get; init; } =
            new(ReadOutcome.Found, new FinanceOfficialInvoiceImportPendingBatchPageV1(Company, [], null));
        public ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1> Detail { get; init; } =
            new(ReadOutcome.NotFound, null);
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchPageV1>> ListAsync(
            FinanceContext context,
            string? cursor,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult(Page);
        }

        public Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1>> GetAsync(
            FinanceContext context,
            Guid batchId,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(Detail);
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
