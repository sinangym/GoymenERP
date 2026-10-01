using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductCard = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

// HTTP serialization/routing with a controlled service double. Not SQL,
// platform global-authority or real-browser acceptance.
public sealed class ProductHttpContractTests
{
    [Theory]
    [InlineData("?roles=sellable,purchasable&roleMatch=all&sort=name&direction=desc&completeness=NotEvaluated", 200)]
    [InlineData("?roles=sellable,sellable", 400)]
    [InlineData("?roles=unknown", 400)]
    [InlineData("?roleMatch=unknown", 400)]
    [InlineData("?sort=unknown", 400)]
    [InlineData("?completeness=unknown", 400)]
    public async Task List_query_vocabulary_is_explicit(string query, int expectedStatus)
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        Assert.Equal(expectedStatus, (int)(await client.GetAsync("/api/v1/products" + query)).StatusCode);
        Assert.Equal(expectedStatus == 200 ? 1 : 0, service.Reads);
    }

    [Fact]
    public async Task Reference_options_report_unavailable_catalogs_without_inventing_choices()
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        var result = await client.GetFromJsonAsync<ProductReferenceOptions>(
            $"/api/v1/products/reference-options?groupId={service.Group.Id}");
        Assert.Empty(result!.Families);
        Assert.Contains("families", result.UnavailableCatalogs);
        Assert.Equal(service.Group.Id, result.GroupId);
    }

    [Fact]
    public async Task Duplicate_candidates_bind_the_name_and_exclusion_and_answer_the_exact_name_rule()
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        var excluded = Guid.NewGuid();
        using var response = await client.GetAsync(
            $"{ProductDuplicateRules.Path}?name=%20%20Sac%20&excludeProductId={excluded:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(["rule", "name", "exactName", "hasMore"], root.EnumerateObject().Select(member => member.Name));
        Assert.Equal("exactName", root.GetProperty("rule").GetString());
        // The server compared the name the write path would store: trimmed.
        Assert.Equal("Sac", root.GetProperty("name").GetString());
        Assert.Equal(service.Card.Product.Id, root.GetProperty("exactName")[0].GetProperty("id").GetGuid());
        Assert.False(root.GetProperty("hasMore").GetBoolean());
        Assert.Equal(excluded, service.LastDuplicateQuery!.ExcludeProductId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?name=")]
    [InlineData("?name=%20%20")]
    [InlineData("?name=Sac%01")]
    [InlineData("?name=Sac&excludeProductId=00000000-0000-0000-0000-000000000000")]
    [InlineData("?name=Sac&excludeProductId=not-a-uuid")]
    public async Task Duplicate_candidates_refuse_a_name_the_write_path_refuses_or_an_empty_exclusion(string query)
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        using var response = await client.GetAsync(ProductDuplicateRules.Path + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(service.LastDuplicateQuery);
    }

    [Fact]
    public async Task Duplicate_candidates_without_the_global_read_disclose_nothing()
    {
        var service = new Service { Denied = true };
        await using var app = await StartAsync(service);
        using var client = Client(app);
        using var response = await client.GetAsync($"{ProductDuplicateRules.Path}?name=Sac");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Sac", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_and_read_use_one_exact_casing_version_and_time_contract()
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        var response = await client.PostAsJsonAsync("/api/v1/products", new CreateProductDraftRequest("Sac", service.Group.Id));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProductMutationResponse>();
        Assert.Equal("Draft", result!.Card.Product.Lifecycle);
        Assert.Equal("1", result.Card.Product.Version);
        Assert.Equal($"/api/v1/products/{result.Card.Product.Id:D}", response.Headers.Location!.ToString());
        var read = await client.GetFromJsonAsync<ProductCardView>(response.Headers.Location);
        Assert.Equal(result.Card.Product.Id, read!.Product.Id);
        Assert.Equal(result.Card.Product.Version, read.Product.Version);
        Assert.Null(read.Product.Roles.ProductionInput.Value);
        Assert.Equal("not-evaluated", read.Product.Roles.ProductionInput.Provenance);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData("\"companyId\":\"9197acdd-034f-4082-bc66-cebc5c81be53\"")]
    [InlineData("\"lifecycle\":\"Active\"")]
    public async Task Protected_field_injection_does_not_reach_command(string field)
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        var json = $$"""{"name":"Sac","groupId":"{{service.Group.Id}}",{{field}}} """;
        using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/products", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, service.Writes);
    }

    [Fact]
    public async Task Missing_key_does_not_reach_command()
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        client.DefaultRequestHeaders.Remove(ProductApiV1.IdempotencyHeader);
        var response = await client.PostAsJsonAsync("/api/v1/products", new CreateProductDraftRequest("Sac", service.Group.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, service.Writes);
    }

    [Fact]
    public async Task Anonymous_request_never_reaches_product_service()
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/products")).StatusCode);
        Assert.Equal(0, service.Reads);
    }

    [Theory]
    [InlineData("""{"expectedVersion":"1","lifecycle":"Active"}""")]
    [InlineData("""{"expectedVersion":"1","companyId":"9197acdd-034f-4082-bc66-cebc5c81be53"}""")]
    [InlineData("""{"expectedVersion":1}""")]
    [InlineData("""{}""")]
    public async Task An_activation_body_with_an_unknown_missing_or_numeric_member_does_not_reach_command(string json)
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        var path = $"/api/v1/products/{service.Card.Product.Id}/activation";
        using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, body)).StatusCode);
        Assert.Equal(0, service.Activations);

        // Control: the exact body reaches the command on the same route.
        using var exact = new StringContent("""{"expectedVersion":"1"}""", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(path, exact)).StatusCode);
        Assert.Equal(1, service.Activations);
    }

    [Theory]
    [InlineData("deactivation", """{"expectedVersion":"2","reason":"Kalktı","lifecycle":"Inactive"}""")]
    [InlineData("deactivation", """{"expectedVersion":"2","reason":"Kalktı","companyId":"9197acdd-034f-4082-bc66-cebc5c81be53"}""")]
    [InlineData("deactivation", """{"expectedVersion":2,"reason":"Kalktı"}""")]
    [InlineData("deactivation", """{"expectedVersion":"2"}""")]
    [InlineData("deactivation", """{"reason":"Kalktı"}""")]
    [InlineData("reactivation", """{"expectedVersion":"3","reason":"Döndü","lifecycle":"Active"}""")]
    [InlineData("reactivation", """{"expectedVersion":"3"}""")]
    [InlineData("reactivation", """{}""")]
    public async Task A_lifecycle_body_with_an_unknown_missing_or_numeric_member_does_not_reach_command(string route, string json)
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        var path = $"/api/v1/products/{service.Card.Product.Id}/{route}";
        using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, body)).StatusCode);
        Assert.Equal((0, 0), (service.Deactivations, service.Reactivations));

        // Control: the exact body reaches only its own command on the same route, and a refusal is a 409 problem.
        using var exact = new StringContent("""{"expectedVersion":"2","reason":"Gerekçe"}""",
            System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(path, exact);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ProductApiV1.LifecycleConflict, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(route == "deactivation" ? (1, 0) : (0, 1), (service.Deactivations, service.Reactivations));
        Assert.Equal(new ChangeProductLifecycleRequest("2", "Gerekçe"), service.LastLifecycleRequest);
        Assert.Equal(0, service.Activations);
    }

    [Fact]
    public async Task Stale_edit_is_problem_details_with_lossless_versions()
    {
        var service = new Service();
        await using var app = await StartAsync(service);
        using var client = Client(app);
        var response = await client.PatchAsJsonAsync($"/api/v1/products/{service.Card.Product.Id}/draft",
            new EditProductDraftRequest("1", new(), "Düzeltme"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("version_conflict", body.GetProperty("code").GetString());
        Assert.Equal("1", body.GetProperty("expectedVersion").GetString());
        Assert.Equal("9007199254740993", body.GetProperty("currentVersion").GetString());
    }

    [Fact]
    public async Task Storage_projection_failure_is_500_problem_before_a_partial_card_is_written()
    {
        var service = new Service { ProjectionFailure = true };
        await using var app = await StartAsync(service);
        using var client = Client(app);
        var response = await client.GetAsync($"/api/v1/products/{service.Card.Product.Id}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("private-storage-detail", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task History_is_exact_private_no_store_and_exposes_no_audit_identity_metadata()
    {
        var service = new Service();
        var audit = new AuditSource(service.Card.Product.Id);
        await using var app = await StartAsync(service, audit);
        using var client = Client(app);

        var response = await client.GetAsync($"/api/v1/products/{service.Card.Product.Id:D}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(service.Card.Product.Id, body.GetProperty("productId").GetGuid());
        Assert.False(body.GetProperty("truncated").GetBoolean());
        var entry = Assert.Single(body.GetProperty("entries").EnumerateArray());
        Assert.Equal(ProductHistoryApiV1.DraftCreate, entry.GetProperty("operation").GetString());
        Assert.Equal("1", entry.GetProperty("versionAfter").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, entry.GetProperty("versionBefore").ValueKind);
        Assert.Empty(entry.GetProperty("changedFields").EnumerateArray());
        var json = body.GetRawText();
        Assert.DoesNotContain("actor", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correlation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotency", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, audit.Reads);
    }

    [Fact]
    public async Task Missing_or_denied_product_never_reads_audit()
    {
        var service = new Service();
        var audit = new AuditSource(service.Card.Product.Id);
        await using var app = await StartAsync(service, audit);
        using var client = Client(app);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/products/{Guid.NewGuid():D}/history")).StatusCode);
        service.Denied = true;
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/products/{service.Card.Product.Id:D}/history")).StatusCode);
        Assert.Equal(0, audit.Reads);
    }

    [Fact]
    public async Task Anonymous_history_request_never_reaches_product_or_audit()
    {
        var service = new Service();
        var audit = new AuditSource(service.Card.Product.Id);
        await using var app = await StartAsync(service, audit);
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/v1/products/{service.Card.Product.Id:D}/history")).StatusCode);
        Assert.Equal(0, service.Reads);
        Assert.Equal(0, audit.Reads);
    }

    [Fact]
    public async Task History_integrity_fault_is_redacted_by_the_host()
    {
        var service = new Service();
        var audit = new AuditSource(service.Card.Product.Id) { Failure = new InvalidOperationException("private-audit-secret") };
        await using var app = await StartAsync(service, audit);
        using var client = Client(app);

        var response = await client.GetAsync($"/api/v1/products/{service.Card.Product.Id:D}/history");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("private-audit-secret", await response.Content.ReadAsStringAsync());
    }

    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Product-Test-Actor", "controlled-fixture");
        client.DefaultRequestHeaders.Add(ProductApiV1.IdempotencyHeader, Guid.NewGuid().ToString("N"));
        return client;
    }

    private static async Task<WebApplication> StartAsync(Service service, AuditSource? audit = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IProductService>(service);
        builder.Services.AddSingleton<IProductImportServiceV1>(new FailClosedProductImportService());
        builder.Services.AddSingleton<IGroupSharedMasterAuthorizationService,
            UnconfiguredGroupSharedMasterAuthorizationService>();
        builder.Services.AddSingleton<IProductAuditHistorySourceV1>(audit ?? new AuditSource(service.Card.Product.Id));
        builder.Services.AddScoped<ProductHistoryReader>();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapProductEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class FailClosedProductImportService : IProductImportServiceV1
    {
        private static UnauthorizedAccessException Denied() =>
            new("Synthetic Product import is unavailable in this isolated host.");

        public Task<ProductImportPreviewV1> PreviewAsync(ReadOnlyMemory<byte> workbook,
            ProductImportColumnMappingV1 mapping, CancellationToken cancellationToken) => throw Denied();

        public Task<ProductImportResultV1> ApplyAsync(ReadOnlyMemory<byte> workbook,
            ProductImportApplyBindingV1 binding, string idempotencyKey,
            CancellationToken cancellationToken) => throw Denied();
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Product-Test-Actor")
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "controlled-fixture")], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
    }

    private sealed class Service : IProductService
    {
        public ProductCodeGroup Group { get; } = new(Guid.NewGuid(), "Sentetik test grubu", "SAC");
        public ProductCardView Card { get; }
        public int Writes { get; private set; }
        public int Activations { get; private set; }
        public int Reads { get; private set; }
        public bool ProjectionFailure { get; init; }
        public bool Denied { get; set; }
        public Service()
        {
            var product = ProductCard.CreateDraft(Guid.NewGuid(), ProductCode.FromSequence("SAC", 1), "Sac",
                Group.Id, null, null, ProductRoles.NotEvaluated, DateTimeOffset.UtcNow);
            Card = new(ProductDraftView.FromProduct(product,
                completeness: new("NotEvaluated", ["references.templateId"])), new());
        }
        public Task<ProductPage> GetPageAsync(ProductListQuery query, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(new ProductPage([Card.Product], null)); }
        public Task<ProductCardView?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            Reads++;
            if (Denied) throw new UnauthorizedAccessException("synthetic Product read refusal");
            if (ProjectionFailure) { throw new InvalidOperationException("private-storage-detail"); }
            return Task.FromResult<ProductCardView?>(id == Card.Product.Id ? Card : null);
        }
        public Task<IReadOnlyList<ProductCodeGroup>> GetCodeGroupsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductCodeGroup>>([Group]);
        public ProductDuplicateQuery? LastDuplicateQuery { get; private set; }
        public Task<ProductDuplicateCandidates> GetDuplicateCandidatesAsync(ProductDuplicateQuery query,
            CancellationToken cancellationToken)
        {
            Reads++;
            if (Denied) throw new UnauthorizedAccessException("synthetic Product read refusal");
            LastDuplicateQuery = ProductDuplicateRules.Normalize(query);
            return Task.FromResult(new ProductDuplicateCandidates(ProductDuplicateRules.ExactName,
                LastDuplicateQuery.Name!, [Card.Product], false));
        }
        public Task<ProductReferenceOptions> GetReferenceOptionsAsync(ProductReferenceQuery query,
            CancellationToken cancellationToken) => Task.FromResult(new ProductReferenceOptions(
                Group.Id, null, [], [], [], [], [], ["classes", "families", "units", "trackingPolicies", "templates"]));
        public Task<ProductMutationResponse> CreateDraftAsync(CreateProductDraftRequest request, string idempotencyKey,
            CancellationToken cancellationToken)
        { Writes++; return Task.FromResult(new ProductMutationResponse(Card, false)); }
        public Task<ProductMutationResponse> EditDraftAsync(Guid id, EditProductDraftRequest request,
            string idempotencyKey, CancellationToken cancellationToken) =>
            throw new ProductVersionConflictException(1, 9007199254740993L);
        public Task<ProductMutationResponse> ActivateAsync(Guid id, ActivateProductRequest request,
            string idempotencyKey, CancellationToken cancellationToken)
        { Activations++; throw new ProductOperationException(ProductApiV1.LifecycleConflict, 409); }
        public int Deactivations { get; private set; }
        public int Reactivations { get; private set; }
        public ChangeProductLifecycleRequest? LastLifecycleRequest { get; private set; }
        public Task<ProductMutationResponse> DeactivateAsync(Guid id, ChangeProductLifecycleRequest request,
            string idempotencyKey, CancellationToken cancellationToken)
        { Deactivations++; LastLifecycleRequest = request; throw new ProductOperationException(ProductApiV1.LifecycleConflict, 409); }
        public Task<ProductMutationResponse> ReactivateAsync(Guid id, ChangeProductLifecycleRequest request,
            string idempotencyKey, CancellationToken cancellationToken)
        { Reactivations++; LastLifecycleRequest = request; throw new ProductOperationException(ProductApiV1.LifecycleConflict, 409); }
    }

    private sealed class AuditSource(Guid productId) : IProductAuditHistorySourceV1
    {
        public int Reads { get; private set; }
        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<ProductAuditRecordV1>> ReadNewestAsync(Guid requestedProductId, int limit,
            CancellationToken cancellationToken)
        {
            Reads++;
            if (Failure is not null) throw Failure;
            Assert.Equal(productId, requestedProductId);
            Assert.Equal(ProductHistoryApiV1.PageSize + 1, limit);
            var after = System.Text.Json.JsonSerializer.Serialize(new
            {
                product = new { id = productId.ToString("D"), version = "1" },
                references = new { },
            });
            return Task.FromResult<IReadOnlyList<ProductAuditRecordV1>>
            ([new(Guid.Parse("91559482-d93c-46f0-abbb-beb3c07f0136"), productId,
                ProductHistoryApiV1.DraftCreate, new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero),
                null, 1, "Sentetik oluşturma", "{}", after)]);
        }
    }
}
