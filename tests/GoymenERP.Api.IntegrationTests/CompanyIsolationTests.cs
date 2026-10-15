using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CompanyIsolationTests(GoymenErpApiFactory factory)
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

    [Fact]
    public async Task AnonymousRequest_IsRejected()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompanySelector_ReturnsOnlyAuthorizedCompanies()
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA);

        var response = await client.GetAsync("/api/v1/organization/companies");
        var body = await response.Content.ReadAsStringAsync();
        var companies = JsonSerializer.Deserialize<List<CompanySummary>>(
            body,
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = Assert.Single(Assert.IsType<List<CompanySummary>>(companies));
        Assert.Equal(TestCompanyIds.CompanyA, company.Id);
        Assert.Equal(CompanyACode, company.Code);
        Assert.Equal(CompanyALegalName, company.LegalName);
        Assert.False(company.IsPlaceholder);
        Assert.DoesNotContain(CompanyBCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyBLegalName, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestCompanyIds.CompanyB.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CompanyCCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyCLegalName, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestCompanyIds.CompanyC.ToString(), body, StringComparison.OrdinalIgnoreCase);
        AssertDirectoryDoesNotExposeFiscalFields(body);
    }

    [Fact]
    public async Task ProvisionedIdentity_IsResolvedToStableLocalUser()
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA);

        var response = await client.GetAsync("/api/v1/identity/me");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "20000000-0000-0000-0000-000000000001",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            TestCompanyIds.CompanyA.ToString(),
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString(),
            body,
            StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("authenticationSessionBinding").ValueKind);
    }

    [Fact]
    public async Task AuthenticatedButUnprovisionedIdentity_IsDeniedByDefaultPolicy()
    {
        using var client = factory.CreateClientFor(TestUsers.Unprovisioned);

        var response = await client.GetAsync("/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SiteScopedViewer_SeesOnlyAuthorizedHierarchy()
    {
        using var client = factory.CreateClientFor(
            TestUsers.CompanyAViewer,
            TestCompanyIds.CompanyA);

        var sites = await client.GetFromJsonAsync<List<SiteSummary>>(
            "/api/v1/organization/sites",
            JsonOptions);
        var locations = await client.GetFromJsonAsync<PageResult<LocationSummary>>(
            "/api/v1/organization/locations?pageSize=100",
            JsonOptions);
        var workCenters = await client.GetFromJsonAsync<List<WorkCenterSummary>>(
            "/api/v1/organization/work-centers",
            JsonOptions);

        Assert.Equal("SITE-OSM", Assert.Single(Assert.IsType<List<SiteSummary>>(sites)).Code);
        Assert.Equal(8, Assert.IsType<PageResult<LocationSummary>>(locations).TotalCount);
        Assert.Equal(5, Assert.IsType<List<WorkCenterSummary>>(workCenters).Count);
    }

    [Fact]
    public async Task ReadOnlyRole_CannotInvokeWriteEndpoint()
    {
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H0");
        using var client = factory.CreateClientFor(
            TestUsers.CompanyAViewer,
            TestCompanyIds.CompanyA);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            new SetLocationActiveState(
                !location.IsActive,
                location.Version,
                "Yetkisiz yazma güvenlik testi"));
        var after = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H0");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(location, after);
    }

    [Fact]
    public async Task SelectedCompanyHeader_CannotEscalateAuthorization()
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyB);

        var response = await client.GetAsync("/api/v1/organization/locations");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoCompanyBLeak(body);
    }

    [Fact]
    public async Task QueryParameterTampering_DoesNotChangeCompanyScopeOrCount()
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);

        var response = await client.GetAsync(
            $"/api/v1/organization/locations?companyId={TestCompanyIds.CompanyB}&pageSize=100");
        var result = await response.Content.ReadFromJsonAsync<PageResult<LocationSummary>>(
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = Assert.IsType<PageResult<LocationSummary>>(result);
        Assert.Equal(15, page.TotalCount);
        Assert.Equal(15, page.Items.Count);
    }

    [Fact]
    public async Task DirectUrlWithOtherCompanyRecord_ReturnsGenericNotFound()
    {
        var companyBLocation = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyB,
            "OSM-H0");
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);

        var response = await client.GetAsync(
            $"/api/v1/organization/locations/{companyBLocation.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("OSM-H0", body, StringComparison.Ordinal);
        Assert.DoesNotContain(companyBLocation.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        AssertNoCompanyBLeak(body);
    }

    [Fact]
    public async Task CommandWithOtherCompanyRecord_IsRejectedAndDoesNotMutate()
    {
        var before = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyB,
            "OSM-H1");
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/organization/locations/{before.Id}/active-state",
            new SetLocationActiveState(
                !before.IsActive,
                before.Version,
                "Şirketler arası komut izolasyonu testi"));
        var body = await response.Content.ReadAsStringAsync();
        var after = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyB,
            "OSM-H1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, after);
        AssertNoCompanyBLeak(body);
    }

    [Fact]
    public async Task CommandInsideSelectedCompany_UpdatesOnlyThatCompany()
    {
        var companyALocation = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H2");
        var companyBBefore = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyB,
            "OSM-H2");
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/organization/locations/{companyALocation.Id}/active-state",
            new SetLocationActiveState(
                !companyALocation.IsActive,
                companyALocation.Version,
                "Lokasyon durum değişikliği kabul testi"));
        var companyAAfter = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H2");
        var companyBAfter = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyB,
            "OSM-H2");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.Equal(!companyALocation.IsActive, companyAAfter.IsActive);
        Assert.Equal(companyALocation.Version + 1, companyAAfter.Version);
        Assert.Equal(companyBBefore, companyBAfter);
    }

    [Fact]
    public async Task MissingCompanySelection_IsRejectedBeforeDataAccess()
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA);

        var response = await client.GetAsync("/api/v1/organization/sites");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CommandWithoutIdempotencyKey_IsRejectedBeforeMutation()
    {
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H4");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyA);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/v1/organization/locations/{location.Id}/active-state",
            new SetLocationActiveState(
                !location.IsActive,
                location.Version,
                "Eksik idempotency bilgisi testi"));
        var after = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H4");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(location, after);
    }

    [Fact]
    public async Task WorkCenters_AreScopedToSelectedCompany()
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);

        var response = await client.GetAsync("/api/v1/organization/work-centers");
        var workCenters = await response.Content.ReadFromJsonAsync<List<WorkCenterSummary>>(
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, Assert.IsType<List<WorkCenterSummary>>(workCenters).Count);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static void AssertNoCompanyBLeak(string body)
    {
        Assert.DoesNotContain(CompanyBCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyBLegalName, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestCompanyIds.CompanyB.ToString(), body, StringComparison.OrdinalIgnoreCase);
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
