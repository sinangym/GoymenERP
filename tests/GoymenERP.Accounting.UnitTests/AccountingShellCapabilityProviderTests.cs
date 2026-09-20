using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Shell;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// ACC-V0's staged shell declarations: M-07 (periods; the approval and the close as controls), M-04
/// (journals, read-only) and M-01 (queues, read-only), exactly as mapped, every one still unavailable
/// until the ACC-01 browser acceptance and its separate flip PR. The host registry's own rules are proven
/// where the host registers it (central).
/// </summary>
public sealed class AccountingShellCapabilityProviderTests
{
    [Fact]
    public void Only_the_screens_acc01_walked_are_published_and_the_journal_detail_stays_staged()
    {
        var declarations = new AccountingShellCapabilityProvider().GetDeclarations();

        Assert.Equal(7, declarations.Count);
        // ACC-V0c: the flip publishes exactly what the acc01 acceptance walked — the period list, its propose
        // screen, the approve and close controls, the M-04 list and the M-01 queues.
        Assert.Equal(
            [
                AccountingShellCapabilityProvider.JournalListCapabilityId,
                AccountingShellCapabilityProvider.PeriodApproveCapabilityId,
                AccountingShellCapabilityProvider.PeriodCloseCapabilityId,
                AccountingShellCapabilityProvider.PeriodListCapabilityId,
                AccountingShellCapabilityProvider.PeriodProposeCapabilityId,
                AccountingShellCapabilityProvider.PostingQueuesCapabilityId,
            ],
            declarations.Where(declaration => declaration.ImplementationAvailable)
                .Select(declaration => declaration.CapabilityId).Order(StringComparer.Ordinal).ToArray());
        // The journal DETAIL is the one route acc01 never opened (its seed writes nothing and no flow produces a
        // journal before ACC-V1), so it stays closed until a slice accepts it.
        var detail = Assert.Single(declarations,
            item => item.CapabilityId == AccountingShellCapabilityProvider.JournalDetailCapabilityId);
        Assert.False(detail.ImplementationAvailable);
    }

    [Fact]
    public void The_routes_screens_and_narrow_permissions_are_the_cards()
    {
        var provider = new AccountingShellCapabilityProvider();
        var byId = provider.GetDeclarations().ToDictionary(declaration => declaration.CapabilityId, StringComparer.Ordinal);

        Assert.Equal(("ACCOUNTING", "1.0"), (provider.Owner, provider.ContractVersion));
        (string Capability, string Route, string Path, string Screen, ShellCapabilityRoutePrecedence Precedence, string[] Permissions)[] expected =
        [
            ("accounting.periods.list", "accounting.periods", "/muhasebe/donemler", "M-07", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.PeriodsRead]),
            ("accounting.periods.propose", "accounting.periods.new", "/muhasebe/donemler/yeni", "M-07-NEW",
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PeriodsPropose]),
            ("accounting.periods.approve", "accounting.periods", "/muhasebe/donemler", "M-07", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.PeriodsRead, AccountingPermissionManifestV1.PeriodsApprove]),
            ("accounting.periods.close", "accounting.periods", "/muhasebe/donemler", "M-07", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.PeriodsRead, AccountingPermissionManifestV1.PeriodsClose]),
            ("accounting.journals.list", "accounting.journals", "/muhasebe/fisler", "M-04", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.JournalsRead]),
            ("accounting.journals.detail", "accounting.journals.detail", "/muhasebe/fisler/:journalId", "M-04-DETAIL",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, [AccountingPermissionManifestV1.JournalsRead]),
            ("accounting.posting-queues", "accounting.posting-queues", "/muhasebe/kontrol", "M-01",
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PostingQueuesRead]),
        ];

        Assert.Equal(expected.Select(item => item.Capability).Order(StringComparer.Ordinal), byId.Keys.Order(StringComparer.Ordinal));
        foreach (var item in expected)
        {
            var declaration = byId[item.Capability];
            Assert.Equal((item.Route, item.Path, item.Screen, item.Precedence),
                (declaration.RouteId, declaration.RoutePath, declaration.ScreenId, declaration.RoutePrecedence));
            Assert.Equal(item.Permissions, declaration.RequiredPermissions);
            Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
            Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
            Assert.Null(declaration.GlobalProductOperation);
            // Only the owner's own narrow names; the central registry registers exactly these (CARİ).
            Assert.All(declaration.RequiredPermissions, permission => Assert.Contains(permission, AccountingPermissionManifestV1.All));
        }
    }

    [Fact]
    public void The_approval_and_the_close_are_controls_of_the_period_list_that_keep_its_access()
    {
        var declarations = new AccountingShellCapabilityProvider().GetDeclarations();
        var list = Assert.Single(declarations, item => item.CapabilityId == AccountingShellCapabilityProvider.PeriodListCapabilityId);
        foreach (var controlId in new[]
                 {
                     AccountingShellCapabilityProvider.PeriodApproveCapabilityId, AccountingShellCapabilityProvider.PeriodCloseCapabilityId,
                 })
        {
            var control = Assert.Single(declarations, item => item.CapabilityId == controlId);
            Assert.Equal(list.CapabilityId, control.ParentCapabilityId);
            // Exactly the list's route, screen, precedence, context and scope (the registry's control rule).
            Assert.Equal(control with { CapabilityId = list.CapabilityId, ParentCapabilityId = null, RequiredPermissions = list.RequiredPermissions },
                list);
            Assert.Superset(list.RequiredPermissions.ToHashSet(StringComparer.Ordinal),
                control.RequiredPermissions.ToHashSet(StringComparer.Ordinal));
        }

        Assert.Equal(2, declarations.Count(item => item.ParentCapabilityId is not null));
    }

    [Fact]
    public void Every_route_has_its_own_screen_id()
    {
        var routes = new AccountingShellCapabilityProvider().GetDeclarations().Where(item => item.ParentCapabilityId is null).ToArray();

        Assert.Equal(["M-01", "M-04", "M-04-DETAIL", "M-07", "M-07-NEW"], routes.Select(item => item.ScreenId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_frontends_capability_file_names_every_declared_id_and_route()
    {
        // ACC-V0b: src/accounting/capabilities.ts read from the repository, so a drift is red here.
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src/frontend/goymen-erp-web/src/accounting/capabilities.ts"));
        foreach (var declaration in new AccountingShellCapabilityProvider().GetDeclarations())
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
}
