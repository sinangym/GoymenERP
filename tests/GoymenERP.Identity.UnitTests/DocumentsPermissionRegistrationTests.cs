using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ERP-000G / AT-065: the document vault's two names are REGISTERED, not granted — known to the platform, company-wide
/// only, not critical, spelled exactly as the vault's manifest spells them, and carried by no role of the shipped seed.
/// The grant is a human gate (the disposable c02 browser actors are the only holders). Same shape as the Logistics
/// registration.
/// </summary>
public sealed class DocumentsPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    public static TheoryData<string> Permissions => new()
    {
        ErpPermissions.DocumentsFilesRead,
        ErpPermissions.DocumentsFilesUpload,
    };

    [Fact]
    public void The_names_are_spelled_as_the_vault_manifest_spells_them()
    {
        Assert.Equal("documents.files.read", ErpPermissions.DocumentsFilesRead);
        Assert.Equal("documents.files.upload", ErpPermissions.DocumentsFilesUpload);
    }

    [Theory]
    [MemberData(nameof(Permissions))]
    public void The_name_is_registered_once_company_wide_and_not_critical(string permission)
    {
        Assert.True(ErpPermissions.IsKnown(permission));
        Assert.Single(ErpPermissions.All, name => name == permission);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
        Assert.False(ErpPermissions.IsCritical(permission));
    }

    [Theory]
    [MemberData(nameof(Permissions))]
    public void No_seeded_role_carries_it_in_the_shipped_seed(string permission)
    {
        var seedText = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.DoesNotContain(permission, seedText);
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seedText);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("GoymenERP repository root was not found.");
    }
}
