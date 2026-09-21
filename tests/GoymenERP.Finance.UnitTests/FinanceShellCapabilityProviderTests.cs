using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Shell;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FIN-UI-1's shell declarations: F-02 and F-04's routes and the approval control, exactly as mapped,
/// every one PUBLISHED by the FIN-UI-1c flip after the FIN-01 browser acceptance (#332). The host
/// registry's own rules are proven where the host registers it.
/// </summary>
public sealed class FinanceShellCapabilityProviderTests
{
    [Fact]
    public void Every_declaration_is_published_after_the_flip()
    {
        var declarations = new FinanceShellCapabilityProvider().GetDeclarations();

        Assert.Equal(9, declarations.Count);
        Assert.All(declarations, declaration => Assert.True(declaration.ImplementationAvailable));
    }

    [Fact]
    public void The_routes_screens_and_permissions_are_the_cards()
    {
        var provider = new FinanceShellCapabilityProvider();
        var byId = provider.GetDeclarations().ToDictionary(declaration => declaration.CapabilityId, StringComparer.Ordinal);

        Assert.Equal("CHATGPT-BUSINESS/FINANCE", provider.Owner);
        Assert.Equal("1.1", provider.ContractVersion);
        (string Capability, string Route, string Path, string Screen, ShellCapabilityRoutePrecedence Precedence, string[] Permissions)[] expected =
        [
            ("finance.receipts.list", "finance.receipts", "/finans/tahsilatlar", "F-02", ShellCapabilityRoutePrecedence.Static,
                [FinancePermissionNames.DraftRead]),
            ("finance.receipts.draft", "finance.receipts.new", "/finans/tahsilatlar/yeni", "F-02-NEW", ShellCapabilityRoutePrecedence.Static,
                [FinancePermissionNames.DraftSave, ErpPermissions.FinanceBankAccountRead]),
            ("finance.receipts.detail", "finance.receipts.detail", "/finans/tahsilatlar/:sourceId", "F-02-DETAIL",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, [FinancePermissionNames.DraftRead]),
            ("finance.receipts.record", "finance.receipts.detail", "/finans/tahsilatlar/:sourceId", "F-02-DETAIL",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                [FinancePermissionNames.DraftRead, FinancePermissionNames.Record, ErpPermissions.AccountingPeriodsRead]),
            ("finance.receipts.reverse.propose", "finance.receipts.detail", "/finans/tahsilatlar/:sourceId", "F-02-DETAIL",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                [FinancePermissionNames.DraftRead, FinancePermissionNames.ReversePropose]),
            ("finance.receipts.reverse.approve", "finance.receipts.reversal-approvals", "/finans/ters-kayit-onaylari",
                "F-02-REVERSAL-APPROVAL", ShellCapabilityRoutePrecedence.Static,
                [FinancePermissionNames.ReverseApprove]),
            ("finance.bank-accounts.list", "finance.bank-accounts", "/finans/banka-hesaplari", "F-04",
                ShellCapabilityRoutePrecedence.Static, [ErpPermissions.FinanceBankAccountRead]),
            ("finance.bank-accounts.propose", "finance.bank-accounts.new", "/finans/banka-hesaplari/yeni", "F-04-NEW",
                ShellCapabilityRoutePrecedence.Static, [ErpPermissions.FinanceBankAccountPropose]),
            ("finance.bank-accounts.approve", "finance.bank-accounts", "/finans/banka-hesaplari", "F-04",
                ShellCapabilityRoutePrecedence.Static,
                [ErpPermissions.FinanceBankAccountRead, ErpPermissions.FinanceBankAccountApprove]),
        ];

        Assert.Equal(expected.Select(item => item.Capability).Order(StringComparer.Ordinal), byId.Keys.Order(StringComparer.Ordinal));
        foreach (var item in expected)
        {
            var declaration = byId[item.Capability];
            Assert.Equal((item.Route, item.Path, item.Screen, item.Precedence),
                (declaration.RouteId, declaration.RoutePath, declaration.ScreenId, declaration.RoutePrecedence));
            Assert.Equal(item.Permissions, declaration.RequiredPermissions);
            // Finance records are one company's, with no site dimension.
            Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
            Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
            Assert.Null(declaration.GlobalProductOperation);
            Assert.All(declaration.RequiredPermissions, permission => Assert.True(ErpPermissions.IsKnown(permission), permission));
        }
    }

    [Fact]
    public void Money_and_approval_controls_keep_their_parent_access()
    {
        var declarations = new FinanceShellCapabilityProvider().GetDeclarations();
        var list = Assert.Single(declarations, item => item.CapabilityId == FinanceShellCapabilityProvider.BankAccountListCapabilityId);
        var approve = Assert.Single(declarations, item => item.CapabilityId == FinanceShellCapabilityProvider.BankAccountApproveCapabilityId);
        var detail = Assert.Single(declarations, item => item.CapabilityId == FinanceShellCapabilityProvider.ReceiptDetailCapabilityId);
        var record = Assert.Single(declarations, item => item.CapabilityId == FinanceShellCapabilityProvider.ReceiptRecordCapabilityId);
        var reversePropose = Assert.Single(declarations,
            item => item.CapabilityId == FinanceShellCapabilityProvider.ReceiptReverseProposeCapabilityId);

        Assert.Equal(list.CapabilityId, approve.ParentCapabilityId);
        Assert.Equal(approve with { CapabilityId = list.CapabilityId, ParentCapabilityId = null, RequiredPermissions = list.RequiredPermissions },
            list);
        Assert.Superset(list.RequiredPermissions.ToHashSet(StringComparer.Ordinal),
            approve.RequiredPermissions.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(detail.CapabilityId, record.ParentCapabilityId);
        Assert.Superset(detail.RequiredPermissions.ToHashSet(StringComparer.Ordinal),
            record.RequiredPermissions.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(detail.CapabilityId, reversePropose.ParentCapabilityId);
        Assert.Superset(detail.RequiredPermissions.ToHashSet(StringComparer.Ordinal),
            reversePropose.RequiredPermissions.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(3, declarations.Count(item => item.ParentCapabilityId is not null));
    }

    [Fact]
    public void The_frontends_capability_file_names_every_declared_id_and_route()
    {
        // FIN-UI-1b: src/finance/capabilities.ts read from the repository, so a drift is red here.
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src/frontend/goymen-erp-web/src/finance/capabilities.ts"));
        foreach (var declaration in new FinanceShellCapabilityProvider().GetDeclarations())
        {
            Assert.Contains($"'{declaration.CapabilityId}'", source);
            Assert.Contains($"'{declaration.RoutePath}'", source);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Every_route_has_its_own_screen_id_and_the_control_keeps_its_lists()
    {
        var declarations = new FinanceShellCapabilityProvider().GetDeclarations();
        var routes = declarations.Where(item => item.ParentCapabilityId is null).ToArray();

        // The registry refuses a screen id shared by two routes (ValidateUnique on ScreenId).
        Assert.Equal(routes.Length, routes.Select(item => item.ScreenId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["F-02", "F-02-DETAIL", "F-02-NEW", "F-02-REVERSAL-APPROVAL", "F-04", "F-04-NEW"],
            routes.Select(item => item.ScreenId).Order(StringComparer.Ordinal));
    }
}
