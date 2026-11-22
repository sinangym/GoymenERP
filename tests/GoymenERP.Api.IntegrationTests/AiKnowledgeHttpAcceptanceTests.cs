using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-A4 HTTP edges of the knowledge package approval endpoints on the in-memory host. No seeded role holds
/// <c>ai.knowledge.approve</c> (a grant is a human gate), so every authenticated call is a 403 without a module code and nothing
/// is written;
/// header and body rules are answered before any store access.
/// </summary>
public sealed class AiKnowledgeHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private const string Packages = "/api/v1/ai/knowledge/packages";
    private const string Approvals = "/api/v1/ai/knowledge/approvals";

    [Fact]
    public async Task Without_the_approve_permission_the_package_list_is_a_code_less_403()
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(Packages);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        // Code-less at the endpoint: only the platform's generic forbidden problem, never a module code.
        Assert.Equal("forbidden", await CodeAsync(response));
    }

    [Fact]
    public async Task An_approval_without_the_permission_is_a_code_less_403_and_a_missing_key_or_bad_body_is_refused_first()
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
        var body = new { sourceKind = "decision-index", packageSha256 = new string('a', 64) };

        using var forbidden = await client.PostAsJsonAsync(Approvals, body);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("forbidden", await CodeAsync(forbidden));

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        using var withoutKey = await client.PostAsJsonAsync(Approvals, body);
        Assert.Equal(HttpStatusCode.BadRequest, withoutKey.StatusCode);
        Assert.Equal("ai_knowledge_command_metadata_invalid", await CodeAsync(withoutKey));

        client.DefaultRequestHeaders.Add("Idempotency-Key", " padded ");
        using var paddedKey = await client.PostAsJsonAsync(Approvals, body);
        Assert.Equal(HttpStatusCode.BadRequest, paddedKey.StatusCode);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "wpa4-strict-body");
        using var unknownField = await client.PostAsync(Approvals,
            new StringContent("{\"sourceKind\":\"decision-index\",\"packageSha256\":\"x\",\"companyId\":\"c\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);
        Assert.Equal("validation_failed", await CodeAsync(unknownField));

        using var notJson = await client.PostAsync(Approvals, new StringContent("x", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notJson.StatusCode);
    }

    [Fact]
    public async Task Without_a_user_the_endpoints_answer_401()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Packages);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text)) return null;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
