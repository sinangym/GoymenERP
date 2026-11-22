using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16): the two legal deadline names are registered once, company-wide only, not Critical, granted to no
/// seeded role, and each stands alone on its one Settings screen.
/// </summary>
public sealed class SettingsLegalDeadlinePermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Theory]
    [InlineData("settings.legal-deadline-calendar.manage")]
    [InlineData("settings.legal-deadlines.read")]
    public void Each_name_is_registered_once_company_wide_and_not_critical(string name)
    {
        Assert.True(ErpPermissions.IsKnown(name));
        Assert.Single(ErpPermissions.All, candidate => candidate == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.False(ErpPermissions.IsCritical(name));
    }

    [Fact]
    public void The_owner_constants_are_the_central_names()
    {
        Assert.Equal(ErpPermissions.SettingsLegalDeadlineCalendarManage, SettingsLegalDeadlinePermissionsV1.CalendarManage);
        Assert.Equal(ErpPermissions.SettingsLegalDeadlinesRead, SettingsLegalDeadlinePermissionsV1.DeadlinesRead);
    }

    [Fact]
    public void No_seeded_role_carries_either_name()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(SettingsLegalDeadlinePermissionsV1.CalendarManage, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(SettingsLegalDeadlinePermissionsV1.DeadlinesRead, seed, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_screen_stands_on_its_one_name_company_wide_in_one_company()
    {
        var declarations = new SettingsShellCapabilityProvider().GetDeclarations();
        var calendar = Assert.Single(declarations, item => item.CapabilityId == SettingsLegalDeadlinePermissionsV1.CalendarManage);
        Assert.Equal(("/ayarlar/yasal-sure-takvimi", "AY-YST-01"), (calendar.RoutePath, calendar.ScreenId));
        Assert.Equal([SettingsLegalDeadlinePermissionsV1.CalendarManage], calendar.RequiredPermissions);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, calendar.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, calendar.ScopeRequirement);
        Assert.True(calendar.ImplementationAvailable);

        var deadlines = Assert.Single(declarations, item => item.CapabilityId == SettingsLegalDeadlinePermissionsV1.DeadlinesRead);
        Assert.Equal(("/ayarlar/yasal-sureler", "AY-YS-01"), (deadlines.RoutePath, deadlines.ScreenId));
        Assert.Equal([SettingsLegalDeadlinePermissionsV1.DeadlinesRead], deadlines.RequiredPermissions);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, deadlines.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, deadlines.ScopeRequirement);
        Assert.True(deadlines.ImplementationAvailable);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
