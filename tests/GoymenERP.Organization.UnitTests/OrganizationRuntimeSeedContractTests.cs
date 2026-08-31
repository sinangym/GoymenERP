using System.Text.Json;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationRuntimeSeedContractTests
{
    [Fact]
    public void OrganizationSeed_ContainsApprovedCompaniesAndKeepsGinCompanyOnly()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            RepositoryFile("src", "backend", "GoymenERP.Api", "Seed", "organization-seed.json")));
        var companies = document.RootElement
            .GetProperty("OrganizationSeed")
            .GetProperty("Companies")
            .EnumerateArray()
            .ToDictionary(company => company.GetProperty("Code").GetString()!);

        AssertCompany(
            companies["GDC"],
            "10000000-0000-0000-0000-000000000001",
            "Göymen Demir Çelik İnş. San. ve Tic. Ltd. Şti.",
            "4100046586",
            isPlaceholder: false,
            invoiceCapable: true,
            applyTopology: true);
        AssertCompany(
            companies["GSP"],
            "10000000-0000-0000-0000-000000000002",
            "Göymen Sac Profil San. ve Tic. Ltd. Şti.",
            "4101507252",
            isPlaceholder: false,
            invoiceCapable: true,
            applyTopology: false);
        AssertCompany(
            companies["GIN"],
            "10000000-0000-0000-0000-000000000003",
            "Göymen İnşaat",
            "1111111111",
            isPlaceholder: true,
            invoiceCapable: false,
            applyTopology: false);
        Assert.Equal(3, companies.Count);
        Assert.Single(companies.Values, company =>
            company.GetProperty("ApplyTopologyTemplates").GetBoolean());
    }

    [Fact]
    public void IdentitySeed_AssignsExistingGroupAdministratorToGinCompanyScope()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            RepositoryFile("src", "backend", "GoymenERP.Api", "Seed", "identity-seed.json")));
        var assignment = Assert.Single(
            document.RootElement
            .GetProperty("IdentitySeed")
            .GetProperty("Assignments")
            .EnumerateArray(),
            candidate =>
                candidate.GetProperty("Id").GetString()
                == "40000000-0000-0000-0000-000000000007");

        Assert.Equal(
            "20000000-0000-0000-0000-000000000003",
            assignment.GetProperty("UserId").GetString());
        Assert.Equal(
            "30000000-0000-0000-0000-000000000003",
            assignment.GetProperty("RoleId").GetString());
        Assert.Equal(
            "10000000-0000-0000-0000-000000000003",
            assignment.GetProperty("CompanyId").GetString());
        var scope = Assert.Single(assignment.GetProperty("Scopes").EnumerateArray());
        Assert.Equal("Company", scope.GetProperty("Type").GetString());
        Assert.Equal(
            assignment.GetProperty("CompanyId").GetString(),
            scope.GetProperty("Id").GetString());
    }

    private static void AssertCompany(
        JsonElement company,
        string id,
        string legalName,
        string vkn,
        bool isPlaceholder,
        bool invoiceCapable,
        bool applyTopology)
    {
        Assert.Equal(id, company.GetProperty("Id").GetString());
        Assert.Equal(legalName, company.GetProperty("LegalName").GetString());
        Assert.Equal(vkn, company.GetProperty("Vkn").GetString());
        Assert.Equal(isPlaceholder, company.GetProperty("IsPlaceholder").GetBoolean());
        Assert.Equal(invoiceCapable, company.GetProperty("InvoiceCapable").GetBoolean());
        Assert.Equal(
            applyTopology,
            company.GetProperty("ApplyTopologyTemplates").GetBoolean());
    }

    private static string RepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
