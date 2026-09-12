using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GoymenERP.Modules.Product.Api;
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
        Assert.Equal(result.Card.Product, read!.Product);
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

    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Product-Test-Actor", "controlled-fixture");
        client.DefaultRequestHeaders.Add(ProductApiV1.IdempotencyHeader, Guid.NewGuid().ToString("N"));
        return client;
    }

    private static async Task<WebApplication> StartAsync(Service service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IProductService>(service);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapProductEndpoints();
        await app.StartAsync();
        return app;
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
        public int Reads { get; private set; }
        public bool ProjectionFailure { get; init; }
        public Service()
        {
            var product = ProductCard.CreateDraft(Guid.NewGuid(), ProductCode.FromSequence("SAC", 1), "Sac",
                Group.Id, null, null, ProductRoles.NotEvaluated, DateTimeOffset.UtcNow);
            Card = new(ProductDraftView.FromProduct(product), new(), new("NotEvaluated", ["references.templateId"]));
        }
        public Task<ProductPage> GetPageAsync(ProductListQuery query, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(new ProductPage([Card.Product], null)); }
        public Task<ProductCardView?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            Reads++;
            if (ProjectionFailure) { throw new InvalidOperationException("private-storage-detail"); }
            return Task.FromResult<ProductCardView?>(Card);
        }
        public Task<IReadOnlyList<ProductCodeGroup>> GetCodeGroupsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductCodeGroup>>([Group]);
        public Task<ProductMutationResponse> CreateDraftAsync(CreateProductDraftRequest request, string idempotencyKey,
            CancellationToken cancellationToken)
        { Writes++; return Task.FromResult(new ProductMutationResponse(Card, false)); }
        public Task<ProductMutationResponse> EditDraftAsync(Guid id, EditProductDraftRequest request,
            string idempotencyKey, CancellationToken cancellationToken) =>
            throw new ProductVersionConflictException(1, 9007199254740993L);
    }
}
