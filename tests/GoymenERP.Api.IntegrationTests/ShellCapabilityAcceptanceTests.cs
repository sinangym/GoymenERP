using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ShellCapabilityAcceptanceTests(
    GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    [Fact]
    public async Task AnonymousAndUnprovisionedActors_CannotReadCapabilities()
    {
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add(
            "X-Company-Id",
            TestCompanyIds.CompanyA.ToString("D"));
        using var unprovisioned = factory.CreateClientFor(
            TestUsers.Unprovisioned,
            TestCompanyIds.CompanyA);

        using var anonymousResponse = await anonymous.GetAsync(
            "/api/v1/shell/capabilities");
        using var unprovisionedResponse = await unprovisioned.GetAsync(
            "/api/v1/shell/capabilities");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unprovisionedResponse.StatusCode);
        AssertPrivateNoStore(anonymousResponse);
        AssertPrivateNoStore(unprovisionedResponse);
    }

    [Fact]
    public async Task HeaderlessGroupContext_IsAcceptedAndDistinctFromSelectedCompany()
    {
        using var group = factory.CreateClientFor(TestUsers.GroupAdmin);
        using var selected = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);

        using var groupResponse = await group.GetAsync("/api/v1/shell/capabilities");
        using var selectedResponse = await selected.GetAsync("/api/v1/shell/capabilities");

        Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, selectedResponse.StatusCode);
        var groupSnapshot = await groupResponse.Content.ReadFromJsonAsync<JsonElement>();
        var selectedSnapshot = await selectedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(groupSnapshot.GetProperty("capabilities").EnumerateArray());
        Assert.Empty(selectedSnapshot.GetProperty("capabilities").EnumerateArray());
        Assert.NotEqual(
            groupSnapshot.GetProperty("generation").GetString(),
            selectedSnapshot.GetProperty("generation").GetString());
    }

    [Fact]
    public async Task MalformedAndForeignCompanyContext_FailClosed()
    {
        using var malformed = factory.CreateClientFor(TestUsers.CompanyA);
        malformed.DefaultRequestHeaders.Add("X-Company-Id", "not-a-company");
        using var foreign = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyB);

        using var malformedResponse = await malformed.GetAsync(
            "/api/v1/shell/capabilities");
        using var foreignResponse = await foreign.GetAsync(
            "/api/v1/shell/capabilities");

        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
        AssertPrivateNoStore(malformedResponse);
        AssertPrivateNoStore(foreignResponse);
        await AssertProblemCodeAsync(malformedResponse, "company_context_invalid");
    }

    [Fact]
    public void EmptyWhitespaceCommaAndMultipleCompanyHeaders_FailCanonicalParsing()
    {
        Assert.False(ShellEndpoints.TryParseSelectedCompanyId(
            new StringValues(string.Empty),
            out _));
        Assert.False(ShellEndpoints.TryParseSelectedCompanyId(
            new StringValues("   "),
            out _));
        Assert.False(ShellEndpoints.TryParseSelectedCompanyId(
            new StringValues(
                $"{TestCompanyIds.CompanyA:D},{TestCompanyIds.CompanyB:D}"),
            out _));
        Assert.False(ShellEndpoints.TryParseSelectedCompanyId(
            new StringValues(
            [
                TestCompanyIds.CompanyA.ToString("D"),
                TestCompanyIds.CompanyB.ToString("D"),
            ]),
            out _));
    }

    [Theory]
    [InlineData("X-Correlation-Id")]
    [InlineData("X-Causation-Id")]
    public async Task InvalidCorrelationMetadata_IsPrivateAndNonCacheable(
        string invalidHeaderName)
    {
        using var client = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        client.DefaultRequestHeaders.Remove(invalidHeaderName);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            invalidHeaderName,
            "invalid metadata");

        using var response = await client.GetAsync("/api/v1/shell/capabilities");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertPrivateNoStore(response);
        await AssertProblemCodeAsync(response, "correlation_metadata_invalid");
    }

    [Fact]
    public async Task ValidActor_GetsPrivateReplaceAllEmptySnapshotWithoutAuthorityInventory()
    {
        using var client = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);

        using var response = await client.GetAsync("/api/v1/shell/capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(3, root.EnumerateObject().Count());
        Assert.Equal(
            "SHELL-CAPABILITY-V1",
            root.GetProperty("contractVersion").GetString());
        Assert.StartsWith(
            "hmac-sha256:",
            root.GetProperty("generation").GetString(),
            StringComparison.Ordinal);
        Assert.Empty(root.GetProperty("capabilities").EnumerateArray());

        var payload = root.GetRawText();
        Assert.DoesNotContain("permission", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestCompanyIds.CompanyB.ToString("D"), payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StagedCounterpartyCapability_IsNotPublishedBeforeItsUiMount()
    {
        using var client = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);

        using var response = await client.GetAsync("/api/v1/shell/capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.GetProperty("capabilities").EnumerateArray());
    }

    [Fact]
    public async Task HeaderlessGroupContext_ExcludesSingleCompanyOnlyDeclarations()
    {
        var provider = new SingleCompanyOnlyTestProvider();
        var revision = ShellCapabilityRegistry.ComputeCandidateRevision([provider]);
        await using var isolatedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ShellCapabilityAuthorityCatalog>();
                services.RemoveAll<IShellCapabilityProvider>();
                services.AddSingleton(new ShellCapabilityAuthorityCatalog([revision]));
                services.AddSingleton<IShellCapabilityProvider>(provider);
            }));
        using var groupClient = isolatedFactory.CreateClient();
        groupClient.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        using var selectedClient = isolatedFactory.CreateClient();
        selectedClient.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        selectedClient.DefaultRequestHeaders.Add(
            "X-Company-Id",
            TestCompanyIds.CompanyA.ToString("D"));

        using var groupResponse = await groupClient.GetAsync("/api/v1/shell/capabilities");
        using var selectedResponse = await selectedClient.GetAsync(
            "/api/v1/shell/capabilities");

        groupResponse.EnsureSuccessStatusCode();
        selectedResponse.EnsureSuccessStatusCode();
        var groupSnapshot = await groupResponse.Content.ReadFromJsonAsync<JsonElement>();
        var selectedSnapshot = await selectedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(groupSnapshot.GetProperty("capabilities").EnumerateArray());
        Assert.Equal(
            [SingleCompanyOnlyTestProvider.CapabilityId],
            selectedSnapshot.GetProperty("capabilities")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task Generation_IsStableForOneSessionAndChangesAcrossActorOrCompany()
    {
        using var companyAClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        using var companyBClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyB);
        using var otherActorClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var groupClient = factory.CreateClientFor(TestUsers.GroupAdmin);

        var first = await ReadGenerationAsync(companyAClient);
        var repeated = await ReadGenerationAsync(companyAClient);
        var otherCompany = await ReadGenerationAsync(companyBClient);
        var otherActor = await ReadGenerationAsync(otherActorClient);
        var group = await ReadGenerationAsync(groupClient);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, otherCompany);
        Assert.NotEqual(first, otherActor);
        Assert.NotEqual(first, group);
    }

    [Fact]
    public async Task CapabilityRead_HasNoAuditOrIdempotencySideEffects()
    {
        var correlationId = $"shell-{Guid.NewGuid():N}";
        var idempotencyKey = $"shell-{Guid.NewGuid():N}";
        using var client = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

        using var response = await client.GetAsync("/api/v1/shell/capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(correlationId));
        Assert.Empty(await factory.GetCommandExecutionsAsync(idempotencyKey));
    }

    [Fact]
    public async Task InactiveSelectedCompany_FailsClosedWithoutCompanyInventory()
    {
        await using var isolatedFactory = new GoymenErpApiFactory();
        using (var healthClient = isolatedFactory.CreateClient())
        using (var healthResponse = await healthClient.GetAsync("/health/live"))
        {
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        }

        await using (var scope = isolatedFactory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var company = await dbContext.Companies
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == TestCompanyIds.CompanyB);
            dbContext.Entry(company).Property(item => item.IsActive).CurrentValue = false;
            await dbContext.SaveChangesAsync();
        }

        using var client = isolatedFactory.CreateClientFor(
            TestUsers.CompanyB,
            TestCompanyIds.CompanyB);
        using var response = await client.GetAsync("/api/v1/shell/capabilities");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString("D"),
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GSP", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Göymen Sac Profil San. ve Tic. Ltd. Şti.",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("4101507252", body, StringComparison.Ordinal);
        Assert.DoesNotContain("vkn", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invoiceCapable", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadGenerationAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/shell/capabilities");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("generation").GetString()!;
    }

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.True(response.Headers.CacheControl.Private);
        Assert.Equal("no-cache", Assert.Single(response.Headers.Pragma).Name);
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Content.Headers.LastModified);
        Assert.Null(response.Content.Headers.Expires);
    }

    private sealed class SingleCompanyOnlyTestProvider : IShellCapabilityProvider
    {
        public const string CapabilityId = "test.single-company";

        public string Owner => "TEST-SHELL";

        public string ContractVersion => "1.0";

        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
        [
            new ShellCapabilityDeclaration(
                CapabilityId,
                "TEST-SINGLE",
                "/test-single",
                "TEST-SCREEN",
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [ErpPermissions.CounterpartyPartiesRead],
                ImplementationAvailable: true),
        ];
    }
}
