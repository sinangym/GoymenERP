using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Inventory.Application;
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
    private static readonly Guid Draft = Guid.Parse("a3000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Default_and_explicit_finance_list_preserve_the_legacy_json_and_cursor()
    {
        var adapter = new Adapter
        {
            Page = new(NativeApprovalReadOutcomeV1.Found, new NativeApprovalReadPageV1(
                Company,
                [Item()],
                "opaque+/=cursor")),
        };
        using var host = Host([adapter]);
        using var client = Client(host);

        using var defaultList = await client.GetAsync(
            WorkApprovalEndpoints.Path + "?cursor=opaque%2B%2F%3Drequest");
        using var explicitList = await client.GetAsync(
            WorkApprovalEndpoints.Path
            + "?kind=finance.official-invoice-import.v1&cursor=second%2Bcursor");

        Assert.Equal(HttpStatusCode.OK, defaultList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, explicitList.StatusCode);
        AssertPrivateNoStore(defaultList);
        Assert.Equal(("second+cursor", 2), (adapter.Cursor, adapter.ListCalls));

        using var json = JsonDocument.Parse(await defaultList.Content.ReadAsStringAsync());
        Assert.Equal(
            ["companyId", "items", "nextCursor"],
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(
            ["kind", "approvalId", "companyId", "status", "version", "batchVersion",
                "checksum", "templateVersion", "itemCount", "submittedBy", "submittedAtUtc"],
            item.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("Pending", item.GetProperty("status").GetString());
        Assert.Equal(NativeApprovalKindsV1.FinanceOfficialInvoiceImport,
            item.GetProperty("kind").GetString());
        Assert.Equal(Company, json.RootElement.GetProperty("companyId").GetGuid());
    }

    [Fact]
    public async Task Finance_detail_preserves_item_and_preview_without_a_root_discriminator()
    {
        var adapter = new Adapter
        {
            Detail = new(NativeApprovalReadOutcomeV1.Found,
                new FinanceInvoiceImportNativeApprovalReadDetailV1(Item(), Preview())),
        };
        using var host = Host([adapter]);
        using var client = Client(host);

        using var response = await client.GetAsync(
            $"{WorkApprovalEndpoints.Path}/{NativeApprovalKindsV1.FinanceOfficialInvoiceImport}/{Batch:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["item", "preview"],
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(Batch, json.RootElement.GetProperty("item").GetProperty("approvalId").GetGuid());
        Assert.Equal(new string('a', 64),
            json.RootElement.GetProperty("preview").GetProperty("sourceSha256").GetString());
    }

    [Fact]
    public async Task Explicit_sales_list_and_detail_serialize_only_the_sales_variant()
    {
        var adapter = new Adapter
        {
            Kind = NativeApprovalKindsV1.SalesOrderDraft,
            Page = new(NativeApprovalReadOutcomeV1.Found, new NativeApprovalReadPageV1(
                Company, [SalesItem()], "sales+/=cursor")),
            Detail = new(NativeApprovalReadOutcomeV1.Found,
                new SalesOrderDraftNativeApprovalReadDetailV1(SalesItem())),
        };
        using var host = Host([adapter]);
        using var client = Client(host);

        using var list = await client.GetAsync(
            WorkApprovalEndpoints.Path + "?kind=sales.order-draft.v1&cursor=sales%2Brequest");
        using var detail = await client.GetAsync(
            $"{WorkApprovalEndpoints.Path}/{NativeApprovalKindsV1.SalesOrderDraft}/{Draft:D}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        AssertPrivateNoStore(list);
        AssertPrivateNoStore(detail);
        Assert.Equal(("sales+request", 1, 1), (adapter.Cursor, adapter.ListCalls, adapter.GetCalls));

        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(
            ["kind", "approvalId", "companyId", "status", "version", "revision",
                "documentNo", "isOwnDraft", "submittedBy", "submittedAtUtc"],
            item.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(NativeApprovalKindsV1.SalesOrderDraft, item.GetProperty("kind").GetString());
        Assert.Equal("PendingApproval", item.GetProperty("status").GetString());
        Assert.True(item.GetProperty("isOwnDraft").GetBoolean());

        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(["item"],
            detailJson.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(Draft,
            detailJson.RootElement.GetProperty("item").GetProperty("approvalId").GetGuid());
    }

    [Theory]
    [InlineData("inventory.stock-count.v1")]
    [InlineData("inventory.stock-scrap.v1")]
    public async Task Explicit_inventory_list_and_detail_serialize_only_the_selected_variant(string kind)
    {
        var approvalId = Guid.NewGuid();
        var item = InventoryItem(kind, approvalId);
        NativeApprovalReadDetailV1 detail = kind == NativeApprovalKindsV1.InventoryStockCount
            ? new InventoryStockCountNativeApprovalReadDetailV1(item)
            : new InventoryStockScrapNativeApprovalReadDetailV1(item);
        var adapter = new Adapter
        {
            Kind = kind,
            Page = new(NativeApprovalReadOutcomeV1.Found,
                new NativeApprovalReadPageV1(Company, [item], null)),
            Detail = new(NativeApprovalReadOutcomeV1.Found, detail),
        };
        using var host = Host([adapter]);
        using var client = Client(host);

        using var list = await client.GetAsync(WorkApprovalEndpoints.Path + $"?kind={kind}");
        using var itemResponse = await client.GetAsync(
            $"{WorkApprovalEndpoints.Path}/{kind}/{approvalId:D}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, itemResponse.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var serialized = Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(kind, serialized.GetProperty("kind").GetString());
        Assert.Equal(approvalId, serialized.GetProperty("approvalId").GetGuid());
        Assert.Equal("PendingApproval", serialized.GetProperty("status").GetString());
        Assert.Equal("1", serialized.GetProperty("labelVersion").GetString());

        using var detailJson = JsonDocument.Parse(await itemResponse.Content.ReadAsStringAsync());
        Assert.Equal(approvalId,
            detailJson.RootElement.GetProperty("item").GetProperty("approvalId").GetGuid());
    }

    [Fact]
    public async Task Contract_2_emits_inventory_label_code_while_the_legacy_shape_remains_unchanged()
    {
        var approvalId = Guid.NewGuid();
        var adapter = new Adapter
        {
            Kind = NativeApprovalKindsV1.InventoryStockCount,
            OnList = context => new(NativeApprovalReadOutcomeV1.Found,
                new NativeApprovalReadPageV1(Company,
                    [InventoryItem(NativeApprovalKindsV1.InventoryStockCount, approvalId,
                        context.IncludeInventoryLabelCode ? "ETK-D05-0001" : null)], null)),
        };
        using var host = Host([adapter]);
        using var client = Client(host);

        using var legacy = await client.GetAsync(
            WorkApprovalEndpoints.Path + "?kind=inventory.stock-count.v1");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            WorkApprovalEndpoints.Path + "?kind=inventory.stock-count.v1");
        request.Headers.Add(NativeApprovalReadContractV1.Header,
            NativeApprovalReadContractV1.InventoryLabelDisplayVersion);
        using var enriched = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enriched.StatusCode);
        using var legacyJson = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync());
        using var enrichedJson = JsonDocument.Parse(await enriched.Content.ReadAsStringAsync());
        Assert.False(Assert.Single(legacyJson.RootElement.GetProperty("items").EnumerateArray())
            .TryGetProperty("labelCode", out _));
        Assert.Equal("ETK-D05-0001", Assert.Single(
            enrichedJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("labelCode").GetString());
        Assert.Equal(NativeApprovalReadContractV1.InventoryLabelDisplayVersion,
            Assert.Single(enriched.Headers.GetValues(NativeApprovalReadContractV1.Header)));
    }

    [Fact]
    public async Task Contract_2_fails_closed_when_an_inventory_item_has_no_label_code()
    {
        var adapter = new Adapter
        {
            Kind = NativeApprovalKindsV1.InventoryStockCount,
            Page = new(NativeApprovalReadOutcomeV1.Found,
                new NativeApprovalReadPageV1(Company,
                    [InventoryItem(NativeApprovalKindsV1.InventoryStockCount, Guid.NewGuid())],
                    null)),
        };
        using var host = Host([adapter]);
        using var client = Client(host);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            WorkApprovalEndpoints.Path + "?kind=inventory.stock-count.v1");
        request.Headers.Add(NativeApprovalReadContractV1.Header,
            NativeApprovalReadContractV1.InventoryLabelDisplayVersion);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertPrivateNoStore(response);
    }

    [Fact]
    public async Task Inventory_adapters_emit_owner_label_code_only_for_the_opted_in_context()
    {
        var legacyContext = new NativeApprovalReadContextV1(
            Company, "checker", "corr-legacy", null);
        var enrichedContext = legacyContext with { IncludeInventoryLabelCode = true };
        var count = new InventoryStockCountPendingApprovalAdapterV1(new CountOwner());
        var scrap = new InventoryStockScrapPendingApprovalAdapterV1(new ScrapOwner());

        var legacyCount = Assert.IsType<InventoryStockCountNativeApprovalReadItemV1>(
            Assert.Single((await count.ListAsync(legacyContext, null, default)).Resource!.Items));
        var enrichedCount = Assert.IsType<InventoryStockCountNativeApprovalReadItemV1>(
            Assert.Single((await count.ListAsync(enrichedContext, null, default)).Resource!.Items));
        var legacyScrap = Assert.IsType<InventoryStockScrapNativeApprovalReadItemV1>(
            Assert.Single((await scrap.ListAsync(legacyContext, null, default)).Resource!.Items));
        var enrichedScrap = Assert.IsType<InventoryStockScrapNativeApprovalReadItemV1>(
            Assert.Single((await scrap.ListAsync(enrichedContext, null, default)).Resource!.Items));

        Assert.Null(legacyCount.LabelCode);
        Assert.Equal("ETK-D05-COUNT", enrichedCount.LabelCode);
        Assert.Null(legacyScrap.LabelCode);
        Assert.Equal("ETK-D05-SCRAP", enrichedScrap.LabelCode);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData(" 2.0")]
    [InlineData("2.0,3.0")]
    public async Task Invalid_or_ambiguous_work_contract_is_rejected_before_owner_read(string value)
    {
        var adapter = new Adapter { Kind = NativeApprovalKindsV1.InventoryStockCount };
        using var host = Host([adapter]);
        using var client = Client(host);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            WorkApprovalEndpoints.Path + "?kind=inventory.stock-count.v1");
        request.Headers.TryAddWithoutValidation(NativeApprovalReadContractV1.Header, value);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, adapter.ListCalls);
    }

    [Theory]
    [InlineData("Denied", HttpStatusCode.Forbidden)]
    [InlineData("NotFound", HttpStatusCode.NotFound)]
    [InlineData("Unavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task Adapter_failures_are_not_converted_to_empty_success(
        string outcomeName,
        HttpStatusCode expectedStatus)
    {
        var outcome = Enum.Parse<NativeApprovalReadOutcomeV1>(outcomeName);
        var adapter = new Adapter { Page = new(outcome, null) };
        using var host = Host([adapter]);
        using var client = Client(host);

        using var response = await client.GetAsync(WorkApprovalEndpoints.Path);

        Assert.Equal(expectedStatus, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(1, adapter.ListCalls);
    }

    [Fact]
    public async Task Unknown_kind_is_404_and_known_unregistered_is_503_without_an_adapter_call()
    {
        using var host = Host([]);
        using var client = Client(host);

        using var unknown = await client.GetAsync(WorkApprovalEndpoints.Path + "?kind=unknown");
        using var known = await client.GetAsync(
            WorkApprovalEndpoints.Path + "?kind=finance.official-invoice-import.v1");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, known.StatusCode);
        using var problem = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.False(problem.RootElement.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Unknown_detail_kind_is_code_less_404_without_calling_an_adapter()
    {
        var adapter = new Adapter();
        using var host = Host([adapter]);
        using var client = Client(host);

        using var response = await client.GetAsync(
            $"{WorkApprovalEndpoints.Path}/unknown/{Batch:D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, adapter.GetCalls);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(problem.RootElement.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Anonymous_request_is_rejected_before_the_native_reader()
    {
        var adapter = new Adapter();
        using var host = Host([adapter]);
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
    [InlineData("authority")]
    public async Task Post_await_context_or_authority_drift_is_denied_over_http(string drift)
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
                if (drift == "correlation")
                {
                    correlation.Set("changed-correlation", null, "test", null, null);
                }
                return drift == "authority"
                    ? new(NativeApprovalReadOutcomeV1.Denied, null)
                    : new(NativeApprovalReadOutcomeV1.Found,
                        new NativeApprovalReadPageV1(context.CompanyId, [], null));
            },
        };
        using var host = Host([adapter], services =>
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
    public void Production_registration_resolves_one_scoped_adapter_per_known_kind_and_query()
    {
        using var scope = factory.Services.CreateScope();

        var adapters = scope.ServiceProvider.GetServices<INativeApprovalReadAdapterV1>().ToArray();
        Assert.Equal(4, adapters.Length);
        Assert.Collection(adapters.OrderBy(adapter => adapter.Kind, StringComparer.Ordinal),
            finance =>
            {
                Assert.IsType<FinanceInvoiceImportPendingApprovalAdapterV1>(finance);
                Assert.Equal(NativeApprovalKindsV1.FinanceOfficialInvoiceImport, finance.Kind);
            },
            count =>
            {
                Assert.IsType<InventoryStockCountPendingApprovalAdapterV1>(count);
                Assert.Equal(NativeApprovalKindsV1.InventoryStockCount, count.Kind);
            },
            scrap =>
            {
                Assert.IsType<InventoryStockScrapPendingApprovalAdapterV1>(scrap);
                Assert.Equal(NativeApprovalKindsV1.InventoryStockScrap, scrap.Kind);
            },
            sales =>
            {
                Assert.IsType<SalesOrderDraftPendingApprovalAdapterV1>(sales);
                Assert.Equal(NativeApprovalKindsV1.SalesOrderDraft, sales.Kind);
            });
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<NativeApprovalReadQueryV1>());
    }

    private WebApplicationFactory<Program> Host(
        IReadOnlyList<INativeApprovalReadAdapterV1> adapters,
        Action<IServiceCollection>? configure = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INativeApprovalReadAdapterV1>();
            foreach (var adapter in adapters)
            {
                services.AddSingleton(adapter);
            }
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

    private static FinanceInvoiceImportNativeApprovalReadItemV1 Item() => new(
        Batch,
        Company,
        "Pending",
        "1",
        "source-version-7",
        new string('a', 64),
        FinanceOfficialInvoiceImportTemplateV1.Version,
        1,
        "maker",
        DateTimeOffset.Parse("2026-10-05T07:00:00Z"));

    private static SalesOrderDraftNativeApprovalReadItemV1 SalesItem() => new(
        Draft,
        Company,
        "PendingApproval",
        "7",
        "3",
        "SO-2026-001",
        true,
        "sales-maker",
        DateTimeOffset.Parse("2026-10-09T07:00:00Z"));

    private static NativeApprovalReadItemV1 InventoryItem(
        string kind,
        Guid approvalId,
        string? labelCode = null) =>
        kind == NativeApprovalKindsV1.InventoryStockCount
            ? new InventoryStockCountNativeApprovalReadItemV1(
                approvalId, Company, "PendingApproval", "1",
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "-3", "1",
                "Company", Company, "Company", Company, "Free", "100", "97", "Sayım farkı",
                labelCode, "counter", DateTimeOffset.Parse("2026-10-10T07:00:00Z"))
            : new InventoryStockScrapNativeApprovalReadItemV1(
                approvalId, Company, "PendingApproval", "1",
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "6", "1",
                "Company", Company, "Company", Company, "Free", "100", "94", "Hurda",
                labelCode, "recorder", DateTimeOffset.Parse("2026-10-10T08:00:00Z"));

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
        public string Kind { get; init; } = NativeApprovalKindsV1.FinanceOfficialInvoiceImport;
        public Func<NativeApprovalReadContextV1,
            NativeApprovalReadResultV1<NativeApprovalReadPageV1>>? OnList { get; init; }
        public NativeApprovalReadResultV1<NativeApprovalReadPageV1> Page { get; init; } =
            new(NativeApprovalReadOutcomeV1.Found,
                new NativeApprovalReadPageV1(Company, [], null));
        public NativeApprovalReadResultV1<NativeApprovalReadDetailV1> Detail { get; init; } =
            new(NativeApprovalReadOutcomeV1.NotFound, null);
        public string? Cursor { get; private set; }
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(
            NativeApprovalReadContextV1 context,
            string? cursor,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            Cursor = cursor;
            return Task.FromResult(OnList?.Invoke(context) ?? Page);
        }

        public Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>> GetAsync(
            NativeApprovalReadContextV1 context,
            Guid approvalId,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(Detail);
        }
    }

    private sealed class CountOwner : IInventoryStockCountPendingApprovalReadServiceV1
    {
        public Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalPageV1>> ListAsync(
            InventoryPendingApprovalReadContextV1 context,
            string? cursor,
            CancellationToken cancellationToken) => Task.FromResult(new
                InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalPageV1>(
                    InventoryPendingApprovalReadOutcomeV1.Found,
                    new InventoryStockCountPendingApprovalPageV1(Company, [CountSource()], null)));

        public Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalDetailV1>> GetAsync(
            InventoryPendingApprovalReadContextV1 context,
            Guid countId,
            CancellationToken cancellationToken) => Task.FromResult(new
                InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalDetailV1>(
                    InventoryPendingApprovalReadOutcomeV1.Found,
                    new InventoryStockCountPendingApprovalDetailV1(CountSource())));
    }

    private sealed class ScrapOwner : IInventoryStockScrapPendingApprovalReadServiceV1
    {
        public Task<InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalPageV1>> ListAsync(
            InventoryPendingApprovalReadContextV1 context,
            string? cursor,
            CancellationToken cancellationToken) => Task.FromResult(new
                InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalPageV1>(
                    InventoryPendingApprovalReadOutcomeV1.Found,
                    new InventoryStockScrapPendingApprovalPageV1(Company, [ScrapSource()], null)));

        public Task<InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalDetailV1>> GetAsync(
            InventoryPendingApprovalReadContextV1 context,
            Guid scrapId,
            CancellationToken cancellationToken) => Task.FromResult(new
                InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalDetailV1>(
                    InventoryPendingApprovalReadOutcomeV1.Found,
                    new InventoryStockScrapPendingApprovalDetailV1(ScrapSource())));
    }

    private static InventoryStockCountPendingApprovalItemV1 CountSource() => new(
        Guid.Parse("71000000-0000-0000-0000-000000000001"), Company,
        Guid.Parse("71000000-0000-0000-0000-000000000002"), "ETK-D05-COUNT", "1",
        "Company", Company, "Company", Company,
        Guid.Parse("71000000-0000-0000-0000-000000000003"),
        Guid.Parse("71000000-0000-0000-0000-000000000004"),
        "Free", "100", "97", "-3", "Sayım farkı", "counter",
        DateTimeOffset.Parse("2026-10-10T07:00:00Z"));

    private static InventoryStockScrapPendingApprovalItemV1 ScrapSource() => new(
        Guid.Parse("72000000-0000-0000-0000-000000000001"), Company,
        Guid.Parse("72000000-0000-0000-0000-000000000002"), "ETK-D05-SCRAP", "1",
        "Company", Company, "Company", Company,
        Guid.Parse("72000000-0000-0000-0000-000000000003"),
        Guid.Parse("72000000-0000-0000-0000-000000000004"),
        "Free", "100", "6", "94", "Hurda", "recorder",
        DateTimeOffset.Parse("2026-10-10T08:00:00Z"));

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
