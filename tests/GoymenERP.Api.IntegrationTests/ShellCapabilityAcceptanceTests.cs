using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
    }

    [Fact]
    public async Task MissingMalformedAndForeignCompanyContext_FailClosed()
    {
        using var missing = factory.CreateClientFor(TestUsers.CompanyA);
        using var malformed = factory.CreateClientFor(TestUsers.CompanyA);
        malformed.DefaultRequestHeaders.Add("X-Company-Id", "not-a-company");
        using var foreign = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyB);

        using var missingResponse = await missing.GetAsync(
            "/api/v1/shell/capabilities");
        using var malformedResponse = await malformed.GetAsync(
            "/api/v1/shell/capabilities");
        using var foreignResponse = await foreign.GetAsync(
            "/api/v1/shell/capabilities");

        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
        await AssertProblemCodeAsync(missingResponse, "company_context_invalid");
        await AssertProblemCodeAsync(malformedResponse, "company_context_invalid");
    }

    [Fact]
    public async Task ValidActor_GetsPrivateReplaceAllEmptySnapshotWithoutAuthorityInventory()
    {
        using var client = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);

        using var response = await client.GetAsync("/api/v1/shell/capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.True(response.Headers.CacheControl.Private);
        Assert.Equal("no-cache", Assert.Single(response.Headers.Pragma).Name);
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

        var first = await ReadGenerationAsync(companyAClient);
        var repeated = await ReadGenerationAsync(companyAClient);
        var otherCompany = await ReadGenerationAsync(companyBClient);
        var otherActor = await ReadGenerationAsync(otherActorClient);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, otherCompany);
        Assert.NotEqual(first, otherActor);
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
}
