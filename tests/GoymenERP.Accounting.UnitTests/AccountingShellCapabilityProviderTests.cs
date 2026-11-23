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
    public void The_journal_detail_and_trial_balance_are_published_and_the_reversal_actions_stay_staged()
    {
        var declarations = new AccountingShellCapabilityProvider().GetDeclarations();

        Assert.Equal(30, declarations.Count);
        // ACC-V0c published what acc01 walked; the ACC-V1 reversal slice adds the journal DETAIL and M-06, whose real
        // read routes exist and which acc01 ACC01-09 walks over the synthetic posted-journal fixture.
        Assert.Equal(
            [
                AccountingShellCapabilityProvider.JournalDetailCapabilityId,
                AccountingShellCapabilityProvider.JournalListCapabilityId,
                AccountingShellCapabilityProvider.NormalDraftApproveCapabilityId,
                AccountingShellCapabilityProvider.NormalDraftCreateCapabilityId,
                AccountingShellCapabilityProvider.NormalDraftDetailCapabilityId,
                AccountingShellCapabilityProvider.PeriodApproveCapabilityId,
                AccountingShellCapabilityProvider.PeriodCloseCapabilityId,
                AccountingShellCapabilityProvider.PeriodListCapabilityId,
                AccountingShellCapabilityProvider.PeriodProposeCapabilityId,
                AccountingShellCapabilityProvider.PeriodReopenCapabilityId,
                AccountingShellCapabilityProvider.PostingQueuesCapabilityId,
                AccountingShellCapabilityProvider.TrialBalanceCapabilityId,
            ],
            declarations.Where(declaration => declaration.ImplementationAvailable)
                .Select(declaration => declaration.CapabilityId).Order(StringComparer.Ordinal).ToArray());
        // The reversal PROPOSE control is not browser-walked yet, the APPROVAL screen has no reopen read yet, and the
        // FIN-S16 M-05 account plan waits for its browser walk.
        Assert.All(
            new[]
            {
                AccountingShellCapabilityProvider.JournalReversalProposeCapabilityId,
                AccountingShellCapabilityProvider.JournalReversalApproveCapabilityId,
                AccountingShellCapabilityProvider.AccountPlanCapabilityId,
                AccountingShellCapabilityProvider.CostDifferencePrepareCapabilityId,
                AccountingShellCapabilityProvider.CostDifferenceApproveCapabilityId,
                AccountingShellCapabilityProvider.InvoiceLineCogsPrepareCapabilityId,
                AccountingShellCapabilityProvider.InvoiceLineCogsApproveCapabilityId,
                AccountingShellCapabilityProvider.SourceVouchersCapabilityId,
                AccountingShellCapabilityProvider.SourceVouchersApproveCapabilityId,
                AccountingShellCapabilityProvider.InvoicedShipmentPrepareCapabilityId,
                AccountingShellCapabilityProvider.InvoicedShipmentApproveCapabilityId,
                AccountingShellCapabilityProvider.InvoicedShipmentReverseCapabilityId,
                AccountingShellCapabilityProvider.InvoicedShipmentReverseApproveCapabilityId,
                AccountingShellCapabilityProvider.PurchaseCostPrepareCapabilityId,
                AccountingShellCapabilityProvider.PurchaseCostApproveCapabilityId,
                AccountingShellCapabilityProvider.PostingMappingsReadCapabilityId,
                AccountingShellCapabilityProvider.PostingMappingsPrepareCapabilityId,
                AccountingShellCapabilityProvider.PostingMappingsActivateCapabilityId,
            },
            id => Assert.False(Assert.Single(declarations, item => item.CapabilityId == id).ImplementationAvailable));
    }

    [Fact]
    public void The_routes_screens_and_narrow_permissions_are_the_cards()
    {
        var provider = new AccountingShellCapabilityProvider();
        var byId = provider.GetDeclarations().ToDictionary(declaration => declaration.CapabilityId, StringComparer.Ordinal);

        // 1.3: the reversal slice changed the declaration set (detail and M-06 published, reversal actions declared staged).
        // 1.4: FIN-S16 declares the staged M-05 account plan route.
        Assert.Equal(("ACCOUNTING", "1.11"), (provider.Owner, provider.ContractVersion));
        (string Capability, string Route, string Path, string Screen, ShellCapabilityRoutePrecedence Precedence, string[] Permissions)[] expected =
        [
            ("accounting.invoiced-shipment-costs.prepare", "accounting.invoiced-shipment-costs.prepare", "/muhasebe/sevk-smm-hazirla",
                "M-04-INVOICEDSHIPMENTPREPARE", ShellCapabilityRoutePrecedence.Static, [NormalJournalPermissionNamesV1.DraftCreate]),
            ("accounting.invoiced-shipment-costs.approve", "accounting.invoiced-shipment-costs.approve", "/muhasebe/sevk-smm-onay",
                "M-04-INVOICEDSHIPMENTAPPROVE", ShellCapabilityRoutePrecedence.Static, [NormalJournalPermissionNamesV1.DraftApprove]),
            ("accounting.invoiced-shipment-costs.reverse-approve", "accounting.invoiced-shipment-costs.reverse-approve", "/muhasebe/sevk-smm-ters-kayit-onay",
                "M-04-INVOICEDSHIPMENTREVERSEAPPROVE", ShellCapabilityRoutePrecedence.Static, [AccountingJournalReversalPermissionNamesV1.Approve]),
            ("accounting.invoiced-shipment-costs.reverse", "accounting.invoiced-shipment-costs.reverse", "/muhasebe/sevk-smm-ters-kayit",
                "M-04-INVOICEDSHIPMENTREVERSE", ShellCapabilityRoutePrecedence.Static, [AccountingJournalReversalPermissionNamesV1.Propose]),
            ("accounting.purchase-cost-differences.prepare", "accounting.purchase-cost-differences.prepare", "/muhasebe/alis-maliyet-farki-hazirla",
                "M-04-PURCHASECOSTPREPARE", ShellCapabilityRoutePrecedence.Static, [AccountingJournalReversalPermissionNamesV1.Propose]),
            ("accounting.purchase-cost-differences.approve", "accounting.purchase-cost-differences.approve", "/muhasebe/alis-maliyet-farki-onay",
                "M-04-PURCHASECOSTAPPROVE", ShellCapabilityRoutePrecedence.Static, [AccountingJournalReversalPermissionNamesV1.Approve]),
            ("accounting.posting-mappings.read", "accounting.posting-mappings.read", "/muhasebe/hesap-eslemeleri",
                "M-05-COGS-MAPPING", ShellCapabilityRoutePrecedence.Static, [AccountingPostingAccountMappingPermissionNamesV1.Read]),
            ("accounting.posting-mappings.prepare", "accounting.posting-mappings.read", "/muhasebe/hesap-eslemeleri",
                "M-05-COGS-MAPPING", ShellCapabilityRoutePrecedence.Static,
                [AccountingPostingAccountMappingPermissionNamesV1.Read, AccountingPostingAccountMappingPermissionNamesV1.Prepare]),
            ("accounting.posting-mappings.activate", "accounting.posting-mappings.read", "/muhasebe/hesap-eslemeleri",
                "M-05-COGS-MAPPING", ShellCapabilityRoutePrecedence.Static,
                [AccountingPostingAccountMappingPermissionNamesV1.Read, AccountingPostingAccountMappingPermissionNamesV1.Activate]),
            ("accounting.source-vouchers.list", "accounting.source-vouchers.list", "/muhasebe/kaynak-fisler",
                "M-04-SOURCE", ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.JournalsRead]),
            ("accounting.source-vouchers.approve", "accounting.source-vouchers.list", "/muhasebe/kaynak-fisler",
                "M-04-SOURCE", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.JournalsRead, NormalJournalPermissionNamesV1.DraftApprove]),
            ("accounting.invoice-line-cogs.prepare", "accounting.invoice-line-cogs.prepare", "/muhasebe/fatura-satiri-smm",
                "M-04-LINE-COGS-PREPARE", ShellCapabilityRoutePrecedence.Static, [AccountingJournalReversalPermissionNamesV1.Propose]),
            ("accounting.invoice-line-cogs.approve", "accounting.invoice-line-cogs.approve", "/muhasebe/fatura-satiri-smm-onay",
                "M-04-LINE-COGS-APPROVE", ShellCapabilityRoutePrecedence.Static, [AccountingJournalReversalPermissionNamesV1.Approve]),
            ("accounting.manufacturing-cost-differences.prepare", "accounting.manufacturing-cost-differences.prepare", "/muhasebe/uretim-maliyet-farklari",
                "M-01-COST-PREPARE", ShellCapabilityRoutePrecedence.Static,
                [AccountingJournalReversalPermissionNamesV1.Propose, AccountingPermissionManifestV1.PeriodsRead, AccountPlanPermissionNamesV1.Read]),
            ("accounting.manufacturing-cost-differences.approve", "accounting.manufacturing-cost-differences.approve", "/muhasebe/uretim-maliyet-farki-onay",
                "M-01-COST-APPROVE", ShellCapabilityRoutePrecedence.Static, [AccountingJournalReversalPermissionNamesV1.Approve]),
            ("accounting.periods.list", "accounting.periods", "/muhasebe/donemler", "M-07", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.PeriodsRead]),
            ("accounting.periods.propose", "accounting.periods.new", "/muhasebe/donemler/yeni", "M-07-NEW",
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PeriodsPropose]),
            ("accounting.periods.approve", "accounting.periods", "/muhasebe/donemler", "M-07", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.PeriodsRead, AccountingPermissionManifestV1.PeriodsApprove]),
            ("accounting.periods.close", "accounting.periods", "/muhasebe/donemler", "M-07", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.PeriodsRead, AccountingPermissionManifestV1.PeriodsClose]),
            ("accounting.periods.reopen", "accounting.periods", "/muhasebe/donemler", "M-07", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.PeriodsRead, AccountingPermissionManifestV1.PeriodsReopen]),
            ("accounting.journals.list", "accounting.journals", "/muhasebe/fisler", "M-04", ShellCapabilityRoutePrecedence.Static,
                [AccountingPermissionManifestV1.JournalsRead]),
            ("accounting.journals.detail", "accounting.journals.detail", "/muhasebe/fisler/:journalId", "M-04-DETAIL",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, [AccountingPermissionManifestV1.JournalsRead]),
            ("accounting.normal-journal-drafts.create", "accounting.normal-journal-drafts.new", "/muhasebe/normal-fisler/yeni", "M-04-NEW",
                ShellCapabilityRoutePrecedence.Static, [NormalJournalPermissionNamesV1.DraftCreate]),
            ("accounting.normal-journal-drafts.detail", "accounting.normal-journal-drafts.detail", "/muhasebe/normal-fisler/:draftId", "M-04-DRAFT",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, [NormalJournalPermissionNamesV1.DraftCreate]),
            ("accounting.normal-journal-drafts.approve", "accounting.normal-journal-drafts.approve", "/muhasebe/normal-fis-onay/:draftId", "M-04-APPROVE",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, [NormalJournalPermissionNamesV1.DraftApprove]),
            ("accounting.posting-queues", "accounting.posting-queues", "/muhasebe/kontrol", "M-01",
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PostingQueuesRead]),
            ("accounting.trial-balance", "accounting.trial-balance", "/muhasebe/mizan", "M-06",
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.JournalsRead]),
            ("accounting.account-plan", "accounting.account-plan", "/muhasebe/hesap-plani", "M-05",
                ShellCapabilityRoutePrecedence.Static, [AccountPlanPermissionNamesV1.Read]),
            ("accounting.journal-reversals.propose", "accounting.journals.detail", "/muhasebe/fisler/:journalId", "M-04-DETAIL",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                [AccountingPermissionManifestV1.JournalsRead, AccountingJournalReversalPermissionNamesV1.Propose]),
            ("accounting.journal-reversals.approve", "accounting.journal-reversals.approve", "/muhasebe/ters-kayit-onay/:proposalId",
                "M-04-REVERSAL-APPROVE", ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                [AccountingJournalReversalPermissionNamesV1.Approve]),
        ];

        Assert.Equal(expected.Select(item => item.Capability).Order(StringComparer.Ordinal), byId.Keys.Order(StringComparer.Ordinal));
        var ownerPermissions = AccountingPermissionManifestV1.All
            .Concat(NormalJournalPermissionNamesV1.All)
            .Concat([AccountingJournalReversalPermissionNamesV1.Propose, AccountingJournalReversalPermissionNamesV1.Approve])
            .Concat(AccountPlanPermissionNamesV1.All)
            .Concat(AccountingPostingAccountMappingPermissionNamesV1.All)
            .ToHashSet(StringComparer.Ordinal);

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
            Assert.All(declaration.RequiredPermissions, permission => Assert.Contains(permission, ownerPermissions));
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
                     AccountingShellCapabilityProvider.PeriodReopenCapabilityId,
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

        Assert.Equal(7, declarations.Count(item => item.ParentCapabilityId is not null));
    }

    [Fact]
    public void Source_voucher_approval_keeps_read_access_and_remains_staged()
    {
        var declarations = new AccountingShellCapabilityProvider().GetDeclarations();
        var list = Assert.Single(declarations, item => item.CapabilityId == AccountingShellCapabilityProvider.SourceVouchersCapabilityId);
        var approve = Assert.Single(declarations, item => item.CapabilityId == AccountingShellCapabilityProvider.SourceVouchersApproveCapabilityId);
        Assert.False(list.ImplementationAvailable);
        Assert.False(approve.ImplementationAvailable);
        Assert.Equal(list.CapabilityId, approve.ParentCapabilityId);
        Assert.Equal(list, approve with
        {
            CapabilityId = list.CapabilityId,
            ParentCapabilityId = null,
            RequiredPermissions = list.RequiredPermissions,
        });
        Assert.Equal([AccountingPermissionManifestV1.JournalsRead, NormalJournalPermissionNamesV1.DraftApprove], approve.RequiredPermissions);
    }

    [Fact]
    public void The_reversal_proposal_is_a_control_of_the_journal_detail_and_the_approval_its_own_route()
    {
        var declarations = new AccountingShellCapabilityProvider().GetDeclarations();
        var detail = Assert.Single(declarations,
            item => item.CapabilityId == AccountingShellCapabilityProvider.JournalDetailCapabilityId);
        var propose = Assert.Single(declarations,
            item => item.CapabilityId == AccountingShellCapabilityProvider.JournalReversalProposeCapabilityId);
        Assert.Equal(detail.CapabilityId, propose.ParentCapabilityId);
        Assert.Equal(propose with
            {
                CapabilityId = detail.CapabilityId,
                ParentCapabilityId = null,
                RequiredPermissions = detail.RequiredPermissions,
                ImplementationAvailable = detail.ImplementationAvailable,
            },
            detail);
        Assert.Superset(detail.RequiredPermissions.ToHashSet(StringComparer.Ordinal),
            propose.RequiredPermissions.ToHashSet(StringComparer.Ordinal));
        // Maker ≠ checker at the declaration level too: proposing never carries the approve name and vice versa.
        Assert.DoesNotContain(AccountingJournalReversalPermissionNamesV1.Approve, propose.RequiredPermissions);
        var approve = Assert.Single(declarations,
            item => item.CapabilityId == AccountingShellCapabilityProvider.JournalReversalApproveCapabilityId);
        Assert.Null(approve.ParentCapabilityId);
        Assert.Equal([AccountingJournalReversalPermissionNamesV1.Approve], approve.RequiredPermissions);
    }

    [Fact]
    public void Every_route_has_its_own_screen_id()
    {
        var routes = new AccountingShellCapabilityProvider().GetDeclarations().Where(item => item.ParentCapabilityId is null).ToArray();

        Assert.Equal(["M-01", "M-01-COST-APPROVE", "M-01-COST-PREPARE", "M-04", "M-04-APPROVE", "M-04-DETAIL", "M-04-DRAFT", "M-04-INVOICEDSHIPMENTAPPROVE", "M-04-INVOICEDSHIPMENTPREPARE", "M-04-INVOICEDSHIPMENTREVERSE", "M-04-INVOICEDSHIPMENTREVERSEAPPROVE", "M-04-LINE-COGS-APPROVE", "M-04-LINE-COGS-PREPARE", "M-04-NEW", "M-04-PURCHASECOSTAPPROVE", "M-04-PURCHASECOSTPREPARE", "M-04-REVERSAL-APPROVE", "M-04-SOURCE", "M-05", "M-05-COGS-MAPPING", "M-06", "M-07", "M-07-NEW"], routes.Select(item => item.ScreenId).Order(StringComparer.Ordinal));
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
