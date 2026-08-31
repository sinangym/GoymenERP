using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CompanyDirectoryAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string CompanyACode = "GDC";
    private const string CompanyALegalName =
        "Göymen Demir Çelik İnş. San. ve Tic. Ltd. Şti.";
    private const string CompanyAVkn = "4100046586";
    private const string CompanyBCode = "GSP";
    private const string CompanyBLegalName =
        "Göymen Sac Profil San. ve Tic. Ltd. Şti.";
    private const string CompanyBVkn = "4101507252";
    private const string CompanyCCode = "GIN";
    private const string CompanyCLegalName = "Göymen İnşaat";
    private const string CompanyCPlaceholderVkn = "1111111111";
    private const string ForgedCompanyCode = "FORGED-FOREIGN-CODE";
    private const string ForgedCompanyLegalName = "FORGED-FOREIGN-LEGAL-NAME";

    private static readonly Guid NoAssignmentUserId = Guid.Parse(
        "20000000-0000-0000-0000-000000000099");

    private static readonly Guid NoAssignmentIdentityId = Guid.Parse(
        "21000000-0000-0000-0000-000000000099");

    [Fact]
    public async Task Directory_RequiresProvisionedIdentity()
    {
        using var anonymousClient = factory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync(
            "/api/v1/organization/companies");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var unprovisionedClient = factory.CreateClientFor(TestUsers.Unprovisioned);
        using var unprovisionedResponse = await unprovisionedClient.GetAsync(
            "/api/v1/organization/companies");
        Assert.Equal(HttpStatusCode.Forbidden, unprovisionedResponse.StatusCode);
    }

    [Fact]
    public async Task Directory_ReturnsOnlyAssignmentDerivedActiveCompaniesInCodeOrder()
    {
        using var companyAClient = factory.CreateClientFor(TestUsers.CompanyA);
        using var companyBClient = factory.CreateClientFor(TestUsers.CompanyB);
        using var groupClient = factory.CreateClientFor(TestUsers.GroupAdmin);

        using var companyAResponse = await companyAClient.GetAsync(
            "/api/v1/organization/companies");
        using var companyBResponse = await companyBClient.GetAsync(
            "/api/v1/organization/companies");
        using var groupResponse = await groupClient.GetAsync(
            "/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.OK, companyAResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, companyBResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);
        AssertPrivateNoStore(companyAResponse);
        AssertPrivateNoStore(companyBResponse);
        AssertPrivateNoStore(groupResponse);

        var companyA = Assert.Single(await ReadCompaniesAsync(companyAResponse));
        var companyB = Assert.Single(await ReadCompaniesAsync(companyBResponse));
        var groupCompanies = await ReadCompaniesAsync(groupResponse);

        AssertCompanyA(companyA);
        AssertCompanyB(companyB);
        Assert.Equal(
            new[]
            {
                TestCompanyIds.CompanyA,
                TestCompanyIds.CompanyC,
                TestCompanyIds.CompanyB,
            },
            groupCompanies.Select(x => x.Id));
        AssertCompanyA(groupCompanies[0]);
        AssertCompanyC(groupCompanies[1]);
        AssertCompanyB(groupCompanies[2]);
        Assert.Equal(
            new[] { CompanyACode, CompanyCCode, CompanyBCode },
            groupCompanies.Select(x => x.Code));
    }

    [Fact]
    public async Task ProvisionedIdentityWithoutAssignment_ReturnsEmptyDirectory()
    {
        await EnsureNoAssignmentUserAsync();
        using var client = factory.CreateClientFor(TestUsers.NoAssignment);

        using var response = await client.GetAsync("/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(await ReadCompaniesAsync(response));
    }

    [Fact]
    public async Task InactiveAuthorizedCompany_IsFilteredFromDirectory()
    {
        await SetCompanyActiveAsync(TestCompanyIds.CompanyB, isActive: false);
        try
        {
            using var groupClient = factory.CreateClientFor(TestUsers.GroupAdmin);
            using var companyBClient = factory.CreateClientFor(TestUsers.CompanyB);
            using var groupResponse = await groupClient.GetAsync(
                "/api/v1/organization/companies");
            using var companyBResponse = await companyBClient.GetAsync(
                "/api/v1/organization/companies");

            Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, companyBResponse.StatusCode);
            AssertPrivateNoStore(groupResponse);
            AssertPrivateNoStore(companyBResponse);
            var groupCompanies = await ReadCompaniesAsync(groupResponse);
            Assert.Equal(2, groupCompanies.Count);
            AssertCompanyA(groupCompanies[0]);
            AssertCompanyC(groupCompanies[1]);
            Assert.Empty(await ReadCompaniesAsync(companyBResponse));
        }
        finally
        {
            await SetCompanyActiveAsync(TestCompanyIds.CompanyB, isActive: true);
        }
    }

    [Fact]
    public async Task ForgedCompanyMetadata_CannotExpandDirectoryOrCreateWriteSideEffects()
    {
        var correlationId = $"directory-read-{Guid.NewGuid():N}";
        var idempotencyKey = $"directory-read-{Guid.NewGuid():N}";
        using var client = factory.CreateClientFor(TestUsers.CompanyA);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        client.DefaultRequestHeaders.Add("X-Allowed-Company-Ids", TestCompanyIds.CompanyB.ToString());
        client.DefaultRequestHeaders.Add("X-Company-Code", ForgedCompanyCode);
        client.DefaultRequestHeaders.Add("X-Company-Legal-Name", ForgedCompanyLegalName);

        using var response = await client.GetAsync(
            $"/api/v1/organization/companies?companyId={TestCompanyIds.CompanyB}"
            + $"&companyCode={ForgedCompanyCode}&legalName={ForgedCompanyLegalName}");
        var body = await response.Content.ReadAsStringAsync();
        var company = Assert.Single(Assert.IsType<List<CompanySummary>>(
            await response.Content.ReadFromJsonAsync<List<CompanySummary>>()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertCompanyA(company);
        AssertNoForeignOrForgedMetadata(body);
        Assert.Empty(await factory.GetAuditEventsIgnoringFiltersAsync(correlationId));
        Assert.Empty(await factory.GetCommandExecutionsAsync(idempotencyKey));

        using var foreignContextClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyB);
        using var foreignContextResponse = await foreignContextClient.GetAsync(
            "/api/v1/organization/companies");
        var foreignContextBody = await foreignContextResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, foreignContextResponse.StatusCode);
        AssertCompanyA(Assert.Single(Assert.IsType<List<CompanySummary>>(
            await foreignContextResponse.Content.ReadFromJsonAsync<List<CompanySummary>>())));
        AssertNoForeignOrForgedMetadata(foreignContextBody);
    }

    private async Task EnsureNoAssignmentUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (await dbContext.ExternalIdentities.AnyAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer
                    && x.Subject == TestUsers.NoAssignment))
        {
            return;
        }

        dbContext.Users.Add(new UserAccount(
            NoAssignmentUserId,
            "Atamasız ERP Test Kullanıcısı",
            email: null));
        dbContext.ExternalIdentities.Add(new ExternalIdentityLink(
            NoAssignmentIdentityId,
            NoAssignmentUserId,
            BootstrapIdentityOptions.Issuer,
            TestUsers.NoAssignment));
        await dbContext.SaveChangesAsync();
    }

    private async Task SetCompanyActiveAsync(Guid companyId, bool isActive)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var company = await dbContext.Companies.SingleAsync(x => x.Id == companyId);
        dbContext.Entry(company).Property(x => x.IsActive).CurrentValue = isActive;
        await dbContext.SaveChangesAsync();
    }

    private static async Task<List<CompanySummary>> ReadCompaniesAsync(
        HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        foreach (var company in document.RootElement.EnumerateArray())
        {
            Assert.Equal(
                ["id", "code", "legalName", "isPlaceholder"],
                company.EnumerateObject().Select(property => property.Name).ToArray());
        }
        AssertDirectoryDoesNotExposeFiscalFields(payload);

        return Assert.IsType<List<CompanySummary>>(
            JsonSerializer.Deserialize<List<CompanySummary>>(
                payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
    }

    private static void AssertCompanyA(CompanySummary company)
    {
        AssertCompany(
            company,
            TestCompanyIds.CompanyA,
            CompanyACode,
            CompanyALegalName,
            isPlaceholder: false);
    }

    private static void AssertCompanyB(CompanySummary company)
    {
        AssertCompany(
            company,
            TestCompanyIds.CompanyB,
            CompanyBCode,
            CompanyBLegalName,
            isPlaceholder: false);
    }

    private static void AssertCompanyC(CompanySummary company)
    {
        AssertCompany(
            company,
            TestCompanyIds.CompanyC,
            CompanyCCode,
            CompanyCLegalName,
            isPlaceholder: true);
    }

    private static void AssertCompany(
        CompanySummary company,
        Guid id,
        string code,
        string legalName,
        bool isPlaceholder)
    {
        Assert.Equal(id, company.Id);
        Assert.Equal(code, company.Code);
        Assert.Equal(legalName, company.LegalName);
        Assert.Equal(isPlaceholder, company.IsPlaceholder);
    }

    private static void AssertNoForeignOrForgedMetadata(string body)
    {
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString(),
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CompanyBCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyBLegalName, body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            TestCompanyIds.CompanyC.ToString(),
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CompanyCCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyCLegalName, body, StringComparison.Ordinal);
        Assert.DoesNotContain(ForgedCompanyCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(ForgedCompanyLegalName, body, StringComparison.Ordinal);
        AssertDirectoryDoesNotExposeFiscalFields(body);
    }

    private static void AssertDirectoryDoesNotExposeFiscalFields(string body)
    {
        Assert.DoesNotContain("vkn", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invoiceCapable", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CompanyAVkn, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyBVkn, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyCPlaceholderVkn, body, StringComparison.Ordinal);
    }
}
