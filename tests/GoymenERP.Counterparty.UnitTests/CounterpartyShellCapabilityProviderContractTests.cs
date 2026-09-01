using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyShellCapabilityProviderContractTests
{
    private const string ContractPath =
        "docs/features/CARI_UI_VERTICAL_SLICE_v1.json";

    [Fact]
    public void StagedListDeclaration_MatchesTheAcceptedRepositoryOwnerSource()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ContractPath)));
        var root = document.RootElement;
        var provider = new CounterpartyShellCapabilityProvider();
        var declarations = provider.GetDeclarations();
        Assert.Equal(2, declarations.Count);
        var declaration = Assert.Single(
            declarations,
            item => item.CapabilityId
                == CounterpartyShellCapabilityProvider.ListCapabilityId);

        Assert.Equal(
            root.GetProperty("owners")
                .GetProperty("businessAndCariFeature")
                .GetString(),
            provider.Owner);
        Assert.Equal(root.GetProperty("contractVersion").GetString(), provider.ContractVersion);

        var route = root.GetProperty("routes")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "cari.list");
        Assert.Equal(route.GetProperty("id").GetString(), declaration.RouteId);
        Assert.Equal(route.GetProperty("path").GetString(), declaration.RoutePath);
        Assert.Equal(route.GetProperty("screen").GetString(), declaration.ScreenId);
        Assert.Equal(route.GetProperty("capability").GetString(), declaration.CapabilityId);
        Assert.Equal(
            "single-or-authorized-multi-company-read",
            route.GetProperty("context").GetString());
        Assert.Equal(
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            declaration.ContextRequirement);

        var capability = root.GetProperty("capabilities")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == declaration.CapabilityId);
        Assert.Equal(
            capability.GetProperty("permissions")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray(),
            declaration.RequiredPermissions);
        Assert.Equal(
            [ErpPermissions.CounterpartyPartiesRead],
            declaration.RequiredPermissions);
        Assert.Equal(
            "company-or-authorized-multi-company-read",
            capability.GetProperty("scope").GetString());
        Assert.Equal(
            ShellCapabilityScopeRequirement.CompanyWide,
            declaration.ScopeRequirement);
        Assert.Equal(
            "blocked",
            root.GetProperty("implementationReadiness").GetString());
        Assert.Equal(
            "no_go",
            root.GetProperty("productionActivation").GetString());
        Assert.False(declaration.ImplementationAvailable);
    }

    [Fact]
    public void StagedCompanyLinkDeclaration_MatchesTheAcceptedRepositoryOwnerSource()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ContractPath)));
        var root = document.RootElement;
        var provider = new CounterpartyShellCapabilityProvider();
        var declaration = Assert.Single(
            provider.GetDeclarations(),
            item => item.CapabilityId
                == CounterpartyShellCapabilityProvider.LinkCapabilityId);

        Assert.Equal(
            root.GetProperty("owners")
                .GetProperty("businessAndCariFeature")
                .GetString(),
            provider.Owner);
        Assert.Equal(root.GetProperty("contractVersion").GetString(), provider.ContractVersion);

        var route = root.GetProperty("routes")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "cari.company-link");
        Assert.Equal(route.GetProperty("id").GetString(), declaration.RouteId);
        Assert.Equal(route.GetProperty("path").GetString(), declaration.RoutePath);
        Assert.Equal(route.GetProperty("screen").GetString(), declaration.ScreenId);
        Assert.Equal(route.GetProperty("capability").GetString(), declaration.CapabilityId);
        Assert.Equal("single-target-company", route.GetProperty("context").GetString());
        Assert.Equal(
            ShellCapabilityContextRequirement.SingleCompany,
            declaration.ContextRequirement);

        var capability = root.GetProperty("capabilities")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == declaration.CapabilityId);
        Assert.Equal(
            capability.GetProperty("permissions")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray(),
            declaration.RequiredPermissions);
        Assert.Equal(
            [
                ErpPermissions.CounterpartyAccountsCreate,
                ErpPermissions.CounterpartyPartiesRead,
            ],
            declaration.RequiredPermissions);
        Assert.Equal("both-company-wide-on-target", capability.GetProperty("scope").GetString());
        Assert.Equal(
            ShellCapabilityScopeRequirement.CompanyWide,
            declaration.ScopeRequirement);
        Assert.Equal("blocked", root.GetProperty("implementationReadiness").GetString());
        Assert.Equal("no_go", root.GetProperty("productionActivation").GetString());
        Assert.False(declaration.ImplementationAvailable);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Göymen ERP repository root was not found.");
    }
}
