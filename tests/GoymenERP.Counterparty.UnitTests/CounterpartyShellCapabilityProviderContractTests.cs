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
        // Existing six declarations plus two standalone staged native identity purposes (Counterparty 1.7).
        Assert.Equal(8, declarations.Count);
        var declaration = Assert.Single(
            declarations,
            item => item.CapabilityId
                == CounterpartyShellCapabilityProvider.ListCapabilityId);

        Assert.Equal(
            root.GetProperty("owners")
                .GetProperty("businessAndCariFeature")
                .GetString(),
            provider.Owner);
        // The accepted 1.6 browse declarations remain unchanged; 1.7 adds only the separately tested staged purposes.
        Assert.Equal("1.6", root.GetProperty("contractVersion").GetString());
        Assert.Equal("1.7", provider.ContractVersion);

        var route = root.GetProperty("routes")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "cari.list");
        Assert.Equal(route.GetProperty("id").GetString(), declaration.RouteId);
        Assert.Equal(route.GetProperty("path").GetString(), declaration.RoutePath);
        Assert.Equal(route.GetProperty("screen").GetString(), declaration.ScreenId);
        Assert.Equal(route.GetProperty("capability").GetString(), declaration.CapabilityId);
        Assert.Equal("static", route.GetProperty("priority").GetString());
        Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
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
        Assert.True(declaration.ImplementationAvailable);
    }

    [Fact]
    public void StagedCreateDeclaration_MatchesTheAcceptedRepositoryOwnerSource()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ContractPath)));
        var root = document.RootElement;
        var provider = new CounterpartyShellCapabilityProvider();
        var declaration = Assert.Single(
            provider.GetDeclarations(),
            item => item.CapabilityId
                == CounterpartyShellCapabilityProvider.CreateCapabilityId);

        Assert.Equal(
            root.GetProperty("owners")
                .GetProperty("businessAndCariFeature")
                .GetString(),
            provider.Owner);
        // The accepted 1.6 browse declarations remain unchanged; 1.7 adds only the separately tested staged purposes.
        Assert.Equal("1.6", root.GetProperty("contractVersion").GetString());
        Assert.Equal("1.7", provider.ContractVersion);

        var route = root.GetProperty("routes")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "cari.create");
        Assert.Equal(route.GetProperty("id").GetString(), declaration.RouteId);
        Assert.Equal(route.GetProperty("path").GetString(), declaration.RoutePath);
        Assert.Equal(route.GetProperty("screen").GetString(), declaration.ScreenId);
        Assert.Equal(route.GetProperty("capability").GetString(), declaration.CapabilityId);
        Assert.Equal("static-before-parameter", route.GetProperty("priority").GetString());
        Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
        Assert.Equal("create-subset", route.GetProperty("scopeVariant").GetString());
        Assert.Equal("single-company", route.GetProperty("context").GetString());
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
            [ErpPermissions.CounterpartyPartiesCreate],
            declaration.RequiredPermissions);
        Assert.Equal("company-wide", capability.GetProperty("scope").GetString());
        Assert.Equal(
            ShellCapabilityScopeRequirement.CompanyWide,
            declaration.ScopeRequirement);
        Assert.Equal("blocked", root.GetProperty("implementationReadiness").GetString());
        Assert.Equal("no_go", root.GetProperty("productionActivation").GetString());
        Assert.True(declaration.ImplementationAvailable);
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
        // The accepted 1.6 browse declarations remain unchanged; 1.7 adds only the separately tested staged purposes.
        Assert.Equal("1.6", root.GetProperty("contractVersion").GetString());
        Assert.Equal("1.7", provider.ContractVersion);

        var route = root.GetProperty("routes")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "cari.company-link");
        Assert.Equal(route.GetProperty("id").GetString(), declaration.RouteId);
        Assert.Equal(route.GetProperty("path").GetString(), declaration.RoutePath);
        Assert.Equal(route.GetProperty("screen").GetString(), declaration.ScreenId);
        Assert.Equal(route.GetProperty("capability").GetString(), declaration.CapabilityId);
        Assert.Equal("static-before-parameter", route.GetProperty("priority").GetString());
        Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
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

    [Fact]
    public void StagedDetailDeclaration_MatchesTheAcceptedRepositoryOwnerSource()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ContractPath)));
        var root = document.RootElement;
        var provider = new CounterpartyShellCapabilityProvider();
        var declaration = Assert.Single(
            provider.GetDeclarations(),
            item => item.CapabilityId
                == CounterpartyShellCapabilityProvider.DetailCapabilityId);

        Assert.Equal(
            root.GetProperty("owners")
                .GetProperty("businessAndCariFeature")
                .GetString(),
            provider.Owner);
        // The accepted 1.6 browse declarations remain unchanged; 1.7 adds only the separately tested staged purposes.
        Assert.Equal("1.6", root.GetProperty("contractVersion").GetString());
        Assert.Equal("1.7", provider.ContractVersion);

        var route = root.GetProperty("routes")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "cari.detail");
        Assert.Equal(route.GetProperty("id").GetString(), declaration.RouteId);
        Assert.Equal(route.GetProperty("path").GetString(), declaration.RoutePath);
        Assert.Equal(route.GetProperty("screen").GetString(), declaration.ScreenId);
        Assert.Equal(route.GetProperty("capability").GetString(), declaration.CapabilityId);
        Assert.Equal("parameter-after-static", route.GetProperty("priority").GetString());
        Assert.Equal(
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            declaration.RoutePrecedence);
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
        Assert.Equal("blocked", root.GetProperty("implementationReadiness").GetString());
        Assert.Equal("no_go", root.GetProperty("productionActivation").GetString());
        Assert.True(declaration.ImplementationAvailable);
    }

    [Fact]
    public void Fns_d121_iban_controls_sit_inside_the_detail_and_need_their_own_permissions()
    {
        var provider = new CounterpartyShellCapabilityProvider();
        var declarations = provider.GetDeclarations();
        var detail = Assert.Single(declarations,
            item => item.CapabilityId == CounterpartyShellCapabilityProvider.DetailCapabilityId);
        var read = Assert.Single(declarations,
            item => item.CapabilityId == CounterpartyShellCapabilityProvider.PartyIbansReadCapabilityId);
        var manage = Assert.Single(declarations,
            item => item.CapabilityId == CounterpartyShellCapabilityProvider.PartyIbansManageCapabilityId);

        Assert.Equal("1.7", provider.ContractVersion);
        foreach (var control in new[] { read, manage })
        {
            Assert.Equal(CounterpartyShellCapabilityProvider.DetailCapabilityId, control.ParentCapabilityId);
            Assert.Equal(detail.RouteId, control.RouteId);
            Assert.Equal(detail.RoutePath, control.RoutePath);
            Assert.Equal(detail.ContextRequirement, control.ContextRequirement);
            Assert.Equal(detail.ScopeRequirement, control.ScopeRequirement);
        }
        Assert.Equal([ErpPermissions.CounterpartyPartiesRead, ErpPermissions.CounterpartyPartyIbansRead],
            read.RequiredPermissions);
        Assert.Equal([ErpPermissions.CounterpartyPartiesRead, ErpPermissions.CounterpartyPartyIbansRead,
            ErpPermissions.CounterpartyPartyIbansManage], manage.RequiredPermissions);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Native_identity_decision_purposes_are_standalone_staged_and_keep_both_native_companywide_permissions(bool tax)
    {
        var provider = new CounterpartyShellCapabilityProvider();
        var capability = tax ? CounterpartyShellCapabilityProvider.TaxIdentityWorkDecisionCapabilityId : CounterpartyShellCapabilityProvider.LegalNameWorkDecisionCapabilityId;
        var value = Assert.Single(provider.GetDeclarations(), item => item.CapabilityId == capability);
        Assert.False(value.ImplementationAvailable); Assert.Null(value.ParentCapabilityId);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, value.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, value.ScopeRequirement);
        Assert.Equal(tax ? CounterpartyShellCapabilityProvider.TaxIdentityWorkDecisionRoutePath : CounterpartyShellCapabilityProvider.LegalNameWorkDecisionRoutePath, value.RoutePath);
        Assert.EndsWith("/:approvalId", value.RoutePath);
        Assert.Equal([tax ? GoymenERP.Modules.Counterparty.Application.Contracts.CounterpartyTaxIdentityPermissionsV1.Approve
            : GoymenERP.Modules.Counterparty.Application.Contracts.CounterpartyLegalNamePermissionsV1.Approve,
            ErpPermissions.CounterpartyPartiesRead], value.RequiredPermissions);
        Assert.True(ErpPermissions.IsCritical(GoymenERP.Modules.Counterparty.Application.Contracts.CounterpartyTaxIdentityPermissionsV1.Approve));
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
