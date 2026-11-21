using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LocalAccountSessionApiTests
{
    [Theory]
    [InlineData("/api/v1/identity/session/two-factor")]
    [InlineData("/api/v1/identity/session/two-factor/enrollment")]
    [InlineData("/api/v1/identity/session/two-factor/enable")]
    [InlineData("/api/v1/identity/session/two-factor/disable")]
    [InlineData("/api/v1/identity/session/two-factor/recovery-codes")]
    public async Task Two_factor_routes_require_a_session_and_do_not_disclose_enrollment_or_recovery_material(string path)
    {
        await using var factory = new LocalAccountApiFactory();
        using var client = factory.CreateClient();
        // KIM-D31: pass the real antiforgery boundary before testing the separate session requirement.
        using var response = path.EndsWith("/two-factor", StringComparison.Ordinal)
            ? await client.GetAsync(path)
            : await PostWithTokenAsync(client, path, await TokenAsync(client),
                JsonContent.Create(new LocalTwoFactorProofV1("1", "SYNTHETIC", "123456", null)));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secretKey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recoveryCodes", text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(SetCookies(response));
    }

    [Theory]
    [InlineData("/api/v1/identity/session/two-factor/enrollment")]
    [InlineData("/api/v1/identity/session/two-factor/enable")]
    [InlineData("/api/v1/identity/session/two-factor/disable")]
    [InlineData("/api/v1/identity/session/two-factor/recovery-codes")]
    public async Task Anonymous_two_factor_writes_reject_missing_or_forged_antiforgery_before_disclosing_material(string path)
    {
        await using var factory = new LocalAccountApiFactory();
        using var client = factory.CreateClient();
        var proof = new LocalTwoFactorProofV1("1", "SYNTHETIC", "123456", null);
        using var missing = await client.PostAsJsonAsync(path, proof);
        await AssertAntiforgeryRefusalAsync(missing);

        // A real anonymous antiforgery cookie is present; a fabricated request token still cannot pass.
        await TokenAsync(client);
        using var forged = await PostWithTokenAsync(client, path, "SENTETIK-forged-token", JsonContent.Create(proof));
        await AssertAntiforgeryRefusalAsync(forged);
        foreach (var response in new[] { missing, forged })
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("secretKey", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recoveryCodes", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(proof.CurrentPassword, text, StringComparison.Ordinal);
            Assert.Empty(SetCookies(response));
        }
    }

    [Fact]
    public async Task A_local_session_without_company_membership_cannot_read_or_enroll_two_factor_credentials()
    {
        await using var factory = new LocalAccountApiFactory();
        await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();
        using (var login = await LoginAsync(client, UserName, Password)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        client.DefaultRequestHeaders.Add(CompanyContextMiddleware.CompanyHeaderName, TestCompanyIds.CompanyA.ToString("D"));
        using var state = await client.GetAsync("/api/v1/identity/session/two-factor");
        Assert.Equal(HttpStatusCode.Forbidden, state.StatusCode);
        using var enrollment = await PostWithTokenAsync(client, "/api/v1/identity/session/two-factor/enrollment", await TokenAsync(client),
            JsonContent.Create(new LocalTwoFactorProofV1("1", Password, null, null)));
        Assert.Equal(HttpStatusCode.Forbidden, enrollment.StatusCode);
        foreach (var response in new[] { state, enrollment })
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain("secretKey", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recoveryCodes", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
