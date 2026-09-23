using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductContracts = GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PurchasingProductCandidateHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly DateOnly EffectiveOn = new(2026, 9, 23);
    private static readonly Guid ProductId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid UnitId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid ProfileId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task Authorized_consumer_gets_exact_purchase_candidates_with_private_no_store()
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(new([
            new(ProductId, "7", "PRD-7", "Bant", UnitId, "kg", ProfileId, "3")
        ], true));
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PurchasingProductCandidateEndpoints.Path
            + "?q=%20BANT%20&effectiveOn=2026-09-23&limit=12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(new ProductContracts.ProductPurchaseCandidateQueryV1(" BANT ", EffectiveOn, 12), Assert.Single(owner.Queries));
        Assert.True(authority.FreshCalls >= 2);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("searchWindowFull").GetBoolean());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("7", item.GetProperty("productVersion").GetString());
        Assert.Equal(UnitId, item.GetProperty("unitId").GetGuid());
        Assert.Equal("kg", item.GetProperty("unitCode").GetString());
        Assert.Equal("3", item.GetProperty("profileVersion").GetString());
    }

    [Fact]
    public async Task Missing_consumer_permission_refuses_before_owner_read()
    {
        var authority = new ControlledAuthority { Granted = false };
        var owner = new StubCandidates(new([], false));
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PurchasingProductCandidateEndpoints.Path + "?effectiveOn=2026-09-23");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(owner.Queries);
    }

    [Fact]
    public async Task Grant_revoked_after_owner_read_is_refused_without_candidate_disclosure()
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(new([
            new(ProductId, "7", "PRIVATE-CODE", "PRIVATE-NAME", UnitId, "kg", ProfileId, "3")
        ], false)) { During = () => authority.Granted = false };
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PurchasingProductCandidateEndpoints.Path + "?effectiveOn=2026-09-23");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.True(authority.FreshCalls >= 2);
        Assert.DoesNotContain("PRIVATE-", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validation", 400, "validation_failed")]
    [InlineData("unavailable", 503, "service_unavailable")]
    public async Task Owner_failures_are_redacted_at_the_purchasing_boundary(string failure, int status, string code)
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(new([], false)) { Failure = failure };
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(PurchasingProductCandidateEndpoints.Path + "?effectiveOn=2026-09-23");

        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private_owner_detail", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Strict_query_refuses_unknown_or_out_of_range_values_before_owner_read()
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(new([], false));
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var unknown = await client.GetAsync(PurchasingProductCandidateEndpoints.Path
            + "?effectiveOn=2026-09-23&unknown=x");
        using var outOfRange = await client.GetAsync(PurchasingProductCandidateEndpoints.Path
            + "?effectiveOn=2026-09-23&limit=101");

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        AssertPrivateNoStore(unknown);
        AssertPrivateNoStore(outOfRange);
        Assert.Empty(owner.Queries);
    }

    private WebApplicationFactory<Program> CreateHost(ControlledAuthority authority, StubCandidates owner) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<ProductContracts.IProductPurchaseCandidatesV1>();
            services.AddSingleton<ProductContracts.IProductPurchaseCandidatesV1>(owner);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubCandidates(ProductContracts.ProductPurchaseCandidatesV1 result)
        : ProductContracts.IProductPurchaseCandidatesV1
    {
        public List<ProductContracts.ProductPurchaseCandidateQueryV1> Queries { get; } = [];
        public Action? During { get; init; }
        public string? Failure { get; init; }

        public Task<ProductContracts.ProductPurchaseCandidatesV1> SearchAsync(
            ProductContracts.ProductPurchaseCandidateQueryV1 query,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);
            During?.Invoke();
            return Failure switch
            {
                "validation" => Task.FromException<ProductContracts.ProductPurchaseCandidatesV1>(
                    new ProductContracts.ProductValidationException([new("q", "private_owner_detail")])),
                "unavailable" => Task.FromException<ProductContracts.ProductPurchaseCandidatesV1>(
                    new ProductContracts.ProductOperationException("private_owner_detail", 503)),
                _ => Task.FromResult(result),
            };
        }
    }

    private sealed class ControlledAuthority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public int FreshCalls { get; private set; }

        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Granted);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            FreshCalls++;
            var allowed = Granted
                && permission == PurchasingPermissionManifestV1.OrdersCreate
                && companyId == TestCompanyIds.CompanyA;
            return Task.FromResult(allowed
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

public sealed class ProductPurchaseEligibilityPortTests
{
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid UnitId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly DateOnly EffectiveOn = new(2026, 9, 23);
    private static ProductPurchaseSelectionV1 Selection => new(ProductId, "7", UnitId, EffectiveOn);

    [Fact]
    public async Task Eligible_and_refused_owner_answers_preserve_the_consumer_contract()
    {
        var source = new StubEligibility(new(
            new(ProductId, "7", "PRD-7", "Bant", new(Guid.NewGuid(), UnitId, "kg", Guid.NewGuid(), "3"), EffectiveOn),
            []));
        var port = new ProductPurchaseEligibilityPort(source);

        var eligible = await port.EvaluateAsync(Selection);
        Assert.Equal((ProductPurchaseEligibilityOutcomeV1.Eligible, "7", 0),
            (eligible.Outcome, eligible.ProductVersion, eligible.Errors.Count));

        source.Answer = new(null, [new("expectedProductVersion", "version_conflict")]);
        var refused = await port.EvaluateAsync(Selection);
        Assert.Equal(ProductPurchaseEligibilityOutcomeV1.Refused, refused.Outcome);
        Assert.Null(refused.ProductVersion);
        Assert.Equal(new PurchasingFieldError("expectedProductVersion", "version_conflict"), Assert.Single(refused.Errors));
    }

    [Theory]
    [InlineData("product-denied", ProductPurchaseEligibilityOutcomeV1.Denied)]
    [InlineData("settings-denied", ProductPurchaseEligibilityOutcomeV1.Denied)]
    [InlineData("product-unavailable", ProductPurchaseEligibilityOutcomeV1.Unavailable)]
    [InlineData("settings-invalid", ProductPurchaseEligibilityOutcomeV1.Unavailable)]
    [InlineData("corrupt", ProductPurchaseEligibilityOutcomeV1.Unavailable)]
    public async Task Owner_failures_are_fail_closed_without_error_metadata(
        string fault,
        ProductPurchaseEligibilityOutcomeV1 expected)
    {
        var source = new StubEligibility(null) { Failure = fault };
        var answer = await new ProductPurchaseEligibilityPort(source).EvaluateAsync(Selection);
        Assert.Equal(expected, answer.Outcome);
        Assert.Null(answer.ProductVersion);
        Assert.Empty(answer.Errors);
    }

    private sealed class StubEligibility(ProductContracts.ProductPurchaseEligibilityV1? answer)
        : ProductContracts.IProductPurchaseEligibilityV1
    {
        public ProductContracts.ProductPurchaseEligibilityV1? Answer { get; set; } = answer;
        public string? Failure { get; init; }

        public Task<ProductContracts.ProductPurchaseEligibilityV1> EvaluateAsync(
            ProductContracts.ProductPurchaseSelectionV1 selection,
            CancellationToken cancellationToken) => Failure switch
        {
            "product-denied" => Task.FromException<ProductContracts.ProductPurchaseEligibilityV1>(
                new ProductContracts.ProductOperationException("private_owner_detail", 403)),
            "settings-denied" => Task.FromException<ProductContracts.ProductPurchaseEligibilityV1>(
                new SettingsPrerequisiteException("settings_access_denied")),
            "product-unavailable" => Task.FromException<ProductContracts.ProductPurchaseEligibilityV1>(
                new ProductContracts.ProductOperationException("private_owner_detail", 503)),
            "settings-invalid" => Task.FromException<ProductContracts.ProductPurchaseEligibilityV1>(
                new SettingsPrerequisiteException("settings_profile_invalid")),
            "corrupt" => Task.FromException<ProductContracts.ProductPurchaseEligibilityV1>(
                new InvalidOperationException("private_owner_detail")),
            _ => Task.FromResult(Answer!),
        };
    }
}
