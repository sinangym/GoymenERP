using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-UI-1: Finance's provider (published since FIN-UI-1c) through the REAL <see cref="ShellCapabilityRegistry"/> — its
/// owner, route, path, screen-id and control rules — alone and beside the other module providers, so
/// that registering it in the host cannot fail at start-up. No database; the host registration and
/// its authority constant are the central writer's.
/// </summary>
public sealed class FinanceShellCapabilityRegistryTests
{
    internal static readonly string[] TrainStagedCapabilities =
    [
        "finance.cash-exchange-book-values.approve", "finance.cash-exchange-book-values.prepare",
        "finance.credits.approve", "finance.credits.propose", "finance.credits.workspace",
        "finance.official-sales-invoices.approve", "finance.official-sales-invoices.record", "finance.official-sales-invoices.withdraw",
        "finance.party-balance-transfers.record",
    ];

    [Fact]
    public void Party_balance_transfer_mount_preserves_the_staged_company_wide_record_and_amount_read_gate()
    {
        var declaration = Assert.Single(new FinanceShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == FinanceShellCapabilityProvider.PartyBalanceTransferCapabilityId);
        Assert.False(declaration.ImplementationAvailable);
        Assert.Equal("/finans/cari-bakiye-devri", declaration.RoutePath);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Equal([FinancePartyBalanceTransferPermissionsV1.Record, FinancePermissionNames.PartyLedgerRead,
            FinancePermissionNames.PartyLedgerAmountRead], declaration.RequiredPermissions);
    }
    [Fact]
    public void The_registry_accepts_the_Finance_provider_with_one_screen_per_route_and_the_approval_as_a_control()
    {
        var provider = new FinanceShellCapabilityProvider();
        var registry = new ShellCapabilityRegistry([provider],
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision([provider])]));

        Assert.Equal(
            [
                ("finance.allocations.list", "F-03", null),
                ("finance.allocations.write", "F-03", "finance.allocations.list"),
                ("finance.bank-accounts.approve", "F-04", "finance.bank-accounts.list"),
                ("finance.bank-accounts.list", "F-04", null),
                ("finance.bank-accounts.propose", "F-04-NEW", null),
                // WP12 (Finance 1.18, FNS-D13/D113): import and match are controls of the statement workspace.
                ("finance.bank-statements.import", "F-13-BANK-STATEMENT", "finance.bank-statements.read"),
                ("finance.bank-statements.match", "F-13-BANK-STATEMENT", "finance.bank-statements.read"),
                ("finance.bank-statements.read", "F-13-BANK-STATEMENT", null),
                ("finance.cash-exchange-book-values.approve", "F-32-EXCHANGE-BOOK-APPROVE", null),
                ("finance.cash-exchange-book-values.prepare", "F-32-EXCHANGE-BOOK-PREPARE", null),
                // FNS-D24 (Finance 1.11): one transfer is one transaction; the commit is a control of the workspace.
                ("finance.cash-transfers.commit", "F-32-CASH-TRANSFERS", "finance.cash-transfers.workspace"),
                ("finance.cash-transfers.workspace", "F-32-CASH-TRANSFERS", null),
                // F-018 L2 (Finance 1.12, FNS-D57): the one-step collateral record is a control of the list.
                ("finance.collaterals.list", "F-18-COLLATERALS", null),
                ("finance.collaterals.record", "F-18-COLLATERALS", "finance.collaterals.list"),
                // F-018 L1 (Finance 1.10, FNS-D27 / FNS-D27a; one step, FNS-D57(3)): propose and approve are controls.
                ("finance.credit-limits.approve", "F-18-CREDIT-LIMITS", "finance.credit-limits.list"),
                ("finance.credit-limits.list", "F-18-CREDIT-LIMITS", null),
                ("finance.credit-limits.propose", "F-18-CREDIT-LIMITS", "finance.credit-limits.list"),
                ("finance.credits.approve", "F-47-CREDITS", "finance.credits.workspace"),
                ("finance.credits.propose", "F-47-CREDITS", "finance.credits.workspace"),
                ("finance.credits.workspace", "F-47-CREDITS", null),
                ("finance.customer-instruments.list", "F-02-INSTRUMENTS", null),
                // FNS-D25 (Finance 1.9): the payment not tied to a party; its commands are controls of the workspace.
                ("finance.expense-payments.approve", "F-25-EXPENSE-PAYMENTS", "finance.expense-payments.workspace"),
                ("finance.expense-payments.draft", "F-25-EXPENSE-PAYMENTS", "finance.expense-payments.workspace"),
                ("finance.expense-payments.record", "F-25-EXPENSE-PAYMENTS", "finance.expense-payments.workspace"),
                ("finance.expense-payments.workspace", "F-25-EXPENSE-PAYMENTS", null),
                ("finance.manual-bank-payments.approve", "F-02-PAYMENT-DETAIL", "finance.manual-bank-payments.detail"),
                ("finance.manual-bank-payments.create", "F-02-PAYMENT-NEW", null),
                ("finance.manual-bank-payments.detail", "F-02-PAYMENT-DETAIL", null),
                ("finance.manual-bank-payments.list", "F-02-PAYMENTS", null),
                ("finance.manual-bank-payments.record", "F-02-PAYMENT-DETAIL", "finance.manual-bank-payments.detail"),
                // API-23 (Finance 1.8): the maker-checker payment reversal controls of the payment detail.
                ("finance.manual-bank-payments.reverse.approve", "F-02-PAYMENT-DETAIL", "finance.manual-bank-payments.detail"),
                ("finance.manual-bank-payments.reverse.propose", "F-02-PAYMENT-DETAIL", "finance.manual-bank-payments.detail"),
                ("finance.official-invoice-import.approve", "F-02-OFFICIAL-INVOICE-APPROVALS", null),
                ("finance.official-invoice-import.upload", "F-02-OFFICIAL-INVOICE-UPLOAD", null),
                ("finance.official-sales-invoices.approve", "F-005-OFFICIAL-APPROVAL", null),
                ("finance.official-sales-invoices.record", "F-005-OFFICIAL", null),
                ("finance.official-sales-invoices.withdraw", "F-005-OFFICIAL", "finance.official-sales-invoices.record"),
                ("finance.open-items.detail", "F-03-OPEN-ITEM-DETAIL", null),
                ("finance.open-items.list", "F-03-OPEN-ITEMS", null),
                // FIN-S9 (Finance 1.13, FNS-D23): propose and approve are controls of the opening balance list.
                ("finance.opening-balances.approve", "F-23-OPENING-BALANCES", "finance.opening-balances.list"),
                ("finance.opening-balances.list", "F-23-OPENING-BALANCES", null),
                ("finance.opening-balances.propose", "F-23-OPENING-BALANCES", "finance.opening-balances.list"),
                ("finance.party-balance-transfers.record", "F-32-PARTY-BALANCE-TRANSFER", null),
                ("finance.party-ledger.accounting", "M-03", null),
                ("finance.party-ledger.cari", "C-04", null),
                ("finance.receipts.detail", "F-02-DETAIL", null),
                ("finance.receipts.draft", "F-02-NEW", null),
                ("finance.receipts.list", "F-02", null),
                ("finance.receipts.record", "F-02-DETAIL", "finance.receipts.detail"),
                ("finance.receipts.reverse.approve", "F-02-REVERSAL-APPROVAL", null),
                ("finance.receipts.reverse.propose", "F-02-DETAIL", "finance.receipts.detail"),
            ],
            registry.Registrations.Select(registration => (registration.Declaration.CapabilityId,
                registration.Declaration.ScreenId, registration.Declaration.ParentCapabilityId)));
        Assert.All(registry.Registrations, registration =>
        {
            Assert.Equal(FinanceShellCapabilityProvider.OwnerId, registration.Owner);
            Assert.Equal(!TrainStagedCapabilities.Contains(registration.Declaration.CapabilityId), registration.Declaration.ImplementationAvailable);
        });
    }

    [Fact]
    public void The_Finance_provider_sits_beside_the_other_module_providers_without_a_collision()
    {
        // Route ids, paths and screen ids are unique across providers too; the combined set must hold.
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new FinanceShellCapabilityProvider(),
        ];

        var registry = new ShellCapabilityRegistry(providers,
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision(providers)]));

        Assert.Equal(51, registry.Registrations.Count(registration => registration.Owner == FinanceShellCapabilityProvider.OwnerId));
    }

    [Fact]
    public void Adding_the_Finance_provider_without_approving_its_revision_fails_closed()
    {
        // Registering the provider alone, without the authority constant, refuses the whole registry:
        // the two must land together (central).
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new FinanceShellCapabilityProvider(),
        ];

        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(providers,
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13AndProductV12])));
    }
}
