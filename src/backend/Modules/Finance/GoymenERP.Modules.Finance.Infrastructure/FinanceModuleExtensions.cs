using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Ai;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Finance's persistence and service registration (FIN-T04b). The host calls
/// <see cref="AddFinancePersistence"/> (with Audit's interceptor through its
/// <c>configureDbContext</c>), <see cref="AddFinanceServices"/> and
/// <see cref="InitializeFinancePersistenceAsync"/>; that wiring is the central writer's.
/// </summary>
public static class FinanceModuleExtensions
{
    public static IServiceCollection AddFinancePersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");
        }

        services.AddDbContext<FinanceDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// FIN-T04b: the real Finance services behind the HTTP surface, replacing the host's unavailable
    /// permission port and bank reader. Every piece is per request (scoped):
    /// <list type="bullet">
    /// <item><see cref="FinancePermissionPort"/>: fresh #276 measurements of the Finance names;</item>
    /// <item><see cref="FinanceBankAccountService"/>: the persisted bank-account reader and master (FS0;
    /// FIN-UI-1), each write audited with the roles of its own permission;</item>
    /// <item><see cref="ReceiptDraftService"/>: the draft service, bound to the Finance money operation
    /// key, audited with the roles that granted the save.</item>
    /// </list>
    /// Needs <see cref="AddFinancePersistence"/>, Audit's collector, the Settings provider and the
    /// Finance party-account port from the host. Without a Settings candidate reader the currency
    /// answer stays unavailable, never a default.
    /// </summary>
    public static IServiceCollection AddFinanceServices(this IServiceCollection services)
    {
        services.AddScoped<FinancePermissionPort>();
        services.AddScoped<IFinancePermissionPort>(provider => provider.GetRequiredService<FinancePermissionPort>());
        // The services are built by the factories below, so no registration statement of this module names
        // the company context: the host's scoped ICompanyContext is resolved, never registered, here.
        services.AddScoped(CreateBankAccountService);
        services.AddScoped<IFinanceBankAccountReader>(provider => provider.GetRequiredService<FinanceBankAccountService>());
        services.AddScoped<IFinanceBankAccountMaster>(provider => provider.GetRequiredService<FinanceBankAccountService>());
        // API-30: the cash account (kasa) master; a definition only, audited with the roles of its own permission.
        services.AddScoped<IFinanceCashAccountMaster>(CreateCashAccountService);
        // FNS-D102: the real internal ledger, including approved openings, protects passivation in the
        // same serializable transaction. The historical fail-closed fallback types are never preferred.
        services.TryAddScoped<IFinanceCashAccountBalanceReader, TreasuryMovementAwareCashAccountBalanceReader>();
        services.TryAddScoped<IFinanceCashAccountBalanceReader, ExpensePaymentAwareCashAccountBalanceReader>();
        services.TryAddScoped<IFinanceCashAccountBalanceReader, NoCashMovementBalanceReader>();
        // API-85a (F018 §L1; FNS-D27/D27a): the party credit limit master. The Sales risk port below reads its
        // effective record; it moves no money.
        services.AddScoped<IFinancePartyCreditLimitMaster>(CreatePartyCreditLimitService);
        // API-85b (F018 §L2; FNS-D57/D31/D58): the customer collateral master, one step. The type and its rate are
        // Settings' (FNS-D31); until a Settings adapter registers its catalog the default answers Unavailable, so a
        // record and the weighted collateral fail closed.
        services.TryAddScoped<IFinanceCollateralTypeCatalogReader>(CreateCollateralTypeCatalogReader);
        services.AddScoped(CreatePartyCollateralService);
        services.AddScoped<IFinancePartyCollateralMaster>(provider => provider.GetRequiredService<FinancePartyCollateralService>());
        services.AddScoped<IFinanceCollateralBankCandidatesV1>(provider => provider.GetRequiredService<FinancePartyCollateralService>());
        // FIN-S9 (FNS-D23): the opening balances, proposed and approved by two people while the Settings opening period
        // is open. Until a Settings adapter registers its reader the default answers Unavailable: every proposal and
        // decision fails closed and the period is never assumed open. Approval records one opening ledger movement.
        services.TryAddScoped<IFinanceOpeningPeriodReader, UnavailableFinanceOpeningPeriodReader>();
        services.AddScoped<IFinanceOpeningBalanceService>(CreateOpeningBalanceService);
        // FIN-SC47 (FNS-D87/FNS-D88/FNS-D93/FNS-D94): the credit account with its imported plan; no grant.
        services.AddScoped<IFinanceCreditAccountService>(CreateCreditAccountService);
        services.TryAddScoped<IFinanceCreditAccountingPeriodPortV1, UnavailableFinanceCreditAccountingPeriodPortV1>();
        services.AddScoped<IFinanceCreditMovementServiceV1>(CreateCreditMovementService);
        services.AddScoped<IFinanceCreditLimitServiceV1>(CreateCreditLimitService);
        // F018 §L3 (FNS-D27 (2), FNS-D31 (4)): the Cari card's Finance panel read — the credit limit, open receivables,
        // collateral, its utilization and the documentary risk from the calculators above. It writes nothing.
        services.AddScoped<IFinancePartyCreditPanelReader>(CreatePartyCreditPanelService);
        services.AddScoped<IManualReceiptService>(CreateReceiptDraftService);
        services.TryAddScoped<IManualBankPaymentDocumentValidationPort,
            UnavailableManualBankPaymentDocumentValidationPort>();
        services.AddScoped<IManualBankPaymentService>(CreateManualBankPaymentService);
        // W1-A (AI-D31, AI-D32): read-only Work inbox sources over Finance's own pending approvals. They decide nothing.
        services.AddScoped<WorkInbox.FinanceWorkInboxFence>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinancePartyCreditLimitWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceManualBankPaymentWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceOfficialSalesInvoiceWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceManualBankPaymentReversalWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceManualReceiptReversalWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceExpensePaymentWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceBankAccountWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceBankAccountIbanChangeWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceCreditAccountWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceBankCreditLimitWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceCreditMovementWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceOpeningBalanceWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceOpeningBalanceReversalWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceCashExchangeBookValueWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceCashAccountWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceCashAccountPassivationWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.FinanceCashAccountReactivationWorkInboxSourceV1>();
        // FNS-D25 (API-25): one expense item. FNS-D102 uses the internal ledger, protected together with
        // each outgoing record in a serializable Finance transaction; unavailable never means sufficient.
        services.TryAddScoped<IFinanceExpenseItemCatalog, SingleExpenseItemCatalog>();
        services.TryAddScoped<IFinanceTreasuryBalanceGuard, InternalLedgerTreasuryBalanceGuard>();
        services.AddScoped<IExpensePaymentService>(CreateExpensePaymentService);
        // FNS-D24 (API-32/34): the transfer between two treasury accounts, one record for both legs. The currency
        // exchange is fail-closed until its rate rule is decided (NEEDS-SINAN); every commit needs the treasury
        // balance guard above to answer Sufficient (FNS-D11).
        services.TryAddScoped<IFinanceCurrencyExchangeRateRule, UnavailableCurrencyExchangeRateRule>();
        services.AddScoped<ICashTransferService>(CreateCashTransferService);
        services.AddScoped(CreateReversalProposalService);
        services.AddScoped<IReversalProposalService>(provider =>
            provider.GetRequiredService<ReversalProposalService>());
        services.AddScoped(CreatePaymentReversalProposalService);
        services.AddScoped<IPaymentReversalProposalService>(provider =>
            provider.GetRequiredService<PaymentReversalProposalService>());
        services.AddScoped<IFinanceAllocationService>(CreateAllocationService);
        services.AddScoped<IFinanceAllocationReadService, FinanceAllocationReadService>();
        // F3a / API-20: a recorded payment applied to payable items, behind the same allocation permissions.
        services.AddScoped<IFinancePaymentAllocationService>(CreatePaymentAllocationService);
        services.AddScoped<IFinancePaymentAllocationReadService, FinancePaymentAllocationReadService>();
        services.AddScoped<IFinanceOpenItemReadServiceV1, FinanceOpenItemReadService>();
        // MOD-01: the Sales-declared customer risk-input port, answered from Finance's own open items, (API-85a) the
        // effective credit limit record and (API-85b) the held received collaterals weighted by the Settings type rate
        // at read time. It carries no authority; the Sales risk read measures its own.
        services.AddScoped<ISalesPartyRiskReadPortV1>(CreateSalesPartyRiskReadPort);
        // FIN-S6 (F-005 D3): the official sales invoice record read by official number + ETTN over Finance's own store,
        // and the Sales-declared API-35 port answered from it (approved records only). Neither carries authority; the
        // Sales match measures its own.
        services.AddScoped<IFinanceOfficialSalesInvoiceReadPortV1>(CreateOfficialSalesInvoiceReadPort);
        services.AddScoped<ISalesOfficialInvoiceReadPortV1>(provider => new FinanceSalesOfficialInvoiceReadPortV1(
            provider.GetRequiredService<IFinanceOfficialSalesInvoiceReadPortV1>()));
        // DEC-FIN-016 first release: the customer cheque / note portfolio, on the same owners and period rule.
        services.AddScoped<ICustomerInstrumentService>(CreateCustomerInstrumentService);
        // The first-release party ledger (cari hesap ekstresi): a read over the recorded facts above, no writer.
        services.AddScoped<IPartyLedgerService>(CreatePartyLedgerService);
        // B-05 / M-03 (FNS-D14, KLN-D3): the party-account read port (Cari Hesaplar list, one account's summary and open
        // items) over the same sub-ledger and the same two ledger authorities; no writer, no new master.
        services.AddScoped<IPartyLedgerReadServiceV1>(CreatePartyLedgerReadService);
        // FIN-T05: the Record's period gate. K1 (#66 5748258299) answered it — occurredOn decides — but the rule
        // that maps the date reads Accounting's period port, which this module cannot see. So the HOST registers
        // OccurredOnFinanceRecordPeriodRule before calling this method and the TryAdd below then does nothing;
        // a composition that registers none still gets the undecided rule, which refuses every Record rather
        // than guessing a period.
        services.TryAddSingleton<IFinanceRecordPeriodRule, UndecidedFinanceRecordPeriodRule>();
        services.AddScoped<IFinanceReversalAllocationGate, FinanceAllocationReversalGate>();
        // WP-A3c: Finance READ tools for the AI catalog. Registration only makes them candidates of the default-deny
        // catalog; Ai:AllowedTools stays empty, and each call still needs ai.panel.use plus the tool's own read grants.
        services.AddScoped<IAiOwnerToolV1>(provider => new FinanceDueOpenItemsAiTool(
            provider.GetRequiredService<IFinanceOpenItemReadServiceV1>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ISettingsMoneyCandidateReader>()));
        services.AddScoped<IAiOwnerToolV1>(provider => new FinanceDueCustomerInstrumentsAiTool(
            provider.GetRequiredService<ICustomerInstrumentService>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ISettingsMoneyCandidateReader>()));
        // AI-D43: the treasury balance comes from the internal Finance ledger through its own authorized read; the
        // latest bank statement (WP12, registered by the host) only adds the difference warning.
        services.AddScoped<IFinanceTreasuryBalanceReadV1, FinanceTreasuryBalanceReadService>();
        services.AddScoped<IAiOwnerToolV1>(provider => new FinanceTreasuryAccountsAiTool(
            provider.GetRequiredService<IFinanceBankAccountReader>(), provider.GetRequiredService<IFinanceCashAccountMaster>(),
            provider.GetRequiredService<IFinanceTreasuryBalanceReadV1>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ISettingsMoneyCandidateReader>(), provider.GetService<IFinanceBankStatementImportServiceV1>()));
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    private static FinancePartyCreditPanelService CreatePartyCreditPanelService(IServiceProvider provider) =>
        new FinancePartyCreditPanelService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<FinancePermissionPort>(),
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            provider.GetRequiredService<IFinanceCollateralTypeCatalogReader>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetService<IFinanceCollateralPolicyReaderV1>());

    private static FinanceSalesPartyRiskReadPortV1 CreateSalesPartyRiskReadPort(IServiceProvider provider) =>
        new FinanceSalesPartyRiskReadPortV1(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<IFinanceCollateralTypeCatalogReader>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetService<IFinanceCollateralPolicyReaderV1>());

    private static FinanceOfficialSalesInvoiceReadPortV1 CreateOfficialSalesInvoiceReadPort(IServiceProvider provider) =>
        new FinanceOfficialSalesInvoiceReadPortV1(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>());

    private static FinanceBankAccountService CreateBankAccountService(IServiceProvider provider)
    {
        var companyContext = provider.GetRequiredService<ICompanyContext>();
        var currentUser = provider.GetRequiredService<ICurrentUser>();
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinanceBankAccountService(
            provider.GetRequiredService<FinanceDbContext>(),
            companyContext,
            currentUser,
            permissions,
            provider.GetService<ISettingsMoneyCandidateReader>()
                ?? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceBankAccountEnvelopes(
                Envelope(provider, ErpPermissions.FinanceBankAccountPropose, permissions, FinanceAction.ProposeBankAccount),
                Envelope(provider, ErpPermissions.FinanceBankAccountApprove, permissions, FinanceAction.ApproveBankAccount)),
            provider.GetRequiredService<TimeProvider>());
    }

    private static FinanceCashAccountService CreateCashAccountService(IServiceProvider provider)
    {
        var companyContext = provider.GetRequiredService<ICompanyContext>();
        var currentUser = provider.GetRequiredService<ICurrentUser>();
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinanceCashAccountService(
            provider.GetRequiredService<FinanceDbContext>(),
            companyContext,
            currentUser,
            permissions,
            provider.GetService<ISettingsMoneyCandidateReader>()
                ?? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceCashAccountEnvelopes(
                Envelope(provider, ErpPermissions.FinanceCashAccountPropose, permissions, FinanceAction.ProposeCashAccount),
                Envelope(provider, ErpPermissions.FinanceCashAccountApprove, permissions, FinanceAction.ApproveCashAccount),
                Envelope(provider, ErpPermissions.FinanceCashAccountPassivationPropose, permissions,
                    FinanceAction.ProposeCashAccountPassivation),
                Envelope(provider, ErpPermissions.FinanceCashAccountPassivationApprove, permissions,
                    FinanceAction.ApproveCashAccountPassivation),
                Envelope(provider, ErpPermissions.FinanceCashAccountReactivationPropose, permissions,
                    FinanceAction.ProposeCashAccountReactivation),
                Envelope(provider, ErpPermissions.FinanceCashAccountReactivationApprove, permissions,
                    FinanceAction.ApproveCashAccountReactivation)),
            provider.GetRequiredService<IFinanceCashAccountBalanceReader>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IAuditTransactionWriterV1>());
    }

    private static FinancePartyCreditLimitService CreatePartyCreditLimitService(IServiceProvider provider)
    {
        var companyContext = provider.GetRequiredService<ICompanyContext>();
        var currentUser = provider.GetRequiredService<ICurrentUser>();
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinancePartyCreditLimitService(
            provider.GetRequiredService<FinanceDbContext>(),
            companyContext,
            currentUser,
            permissions,
            provider.GetService<ISettingsMoneyCandidateReader>()
                ?? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser),
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinancePartyCreditLimitEnvelopes(
                Envelope(provider, ErpPermissions.FinanceCreditLimitsPropose, permissions, FinanceAction.ProposeCreditLimit),
                Envelope(provider, ErpPermissions.FinanceCreditLimitsApprove, permissions, FinanceAction.ApproveCreditLimit)),
            provider.GetRequiredService<TimeProvider>());
    }

    private static FinancePartyCollateralService CreatePartyCollateralService(IServiceProvider provider)
    {
        var companyContext = provider.GetRequiredService<ICompanyContext>();
        var currentUser = provider.GetRequiredService<ICurrentUser>();
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinancePartyCollateralService(
            provider.GetRequiredService<FinanceDbContext>(),
            companyContext,
            currentUser,
            permissions,
            provider.GetService<ISettingsMoneyCandidateReader>()
                ?? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser),
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            provider.GetRequiredService<IFinanceCollateralTypeCatalogReader>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinancePartyCollateralEnvelopes(
                Envelope(provider, ErpPermissions.FinanceCollateralsRecord, permissions, FinanceAction.RecordCollateral)),
            provider.GetRequiredService<TimeProvider>(), provider.GetService<IFinanceCollateralPolicyReaderV1>(),
            provider.GetService<IAuditTransactionWriterV1>());
    }

    private static FinanceOpeningBalanceService CreateOpeningBalanceService(IServiceProvider provider)
    {
        var companyContext = provider.GetRequiredService<ICompanyContext>();
        var currentUser = provider.GetRequiredService<ICurrentUser>();
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinanceOpeningBalanceService(
            provider.GetRequiredService<FinanceDbContext>(),
            companyContext,
            currentUser,
            permissions,
            provider.GetService<ISettingsMoneyCandidateReader>()
                ?? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser),
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            provider.GetRequiredService<IFinanceOpeningPeriodReader>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceOpeningBalanceEnvelopes(
                Envelope(provider, ErpPermissions.FinanceOpeningBalancesPropose, permissions,
                    FinanceAction.ProposeOpeningBalance),
                Envelope(provider, ErpPermissions.FinanceOpeningBalancesApprove, permissions,
                    FinanceAction.ApproveOpeningBalance)),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IFinanceTreasuryBalanceGuard>(),
            provider.GetRequiredService<IAuditTransactionWriterV1>());
    }

    private static FinanceCreditAccountService CreateCreditAccountService(IServiceProvider provider)
    {
        var companyContext = provider.GetRequiredService<ICompanyContext>();
        var currentUser = provider.GetRequiredService<ICurrentUser>();
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinanceCreditAccountService(
            provider.GetRequiredService<FinanceDbContext>(),
            companyContext,
            currentUser,
            permissions,
            provider.GetService<ISettingsMoneyCandidateReaderV2>()
                ?? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceCreditAccountEnvelopes(
                Envelope(provider, ErpPermissions.FinanceCreditsPropose, permissions, FinanceAction.ProposeCredit),
                Envelope(provider, ErpPermissions.FinanceCreditsApprove, permissions, FinanceAction.ApproveCredit)),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ISettingsPrerequisiteProvider>(),
            provider.GetService<IFinanceCreditLenderPortV1>());
    }

    private static FinanceCreditMovementServiceV1 CreateCreditMovementService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinanceCreditMovementServiceV1(provider.GetRequiredService<FinanceDbContext>(), provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(), permissions, provider.GetRequiredService<ISettingsPrerequisiteProvider>(),
            provider.GetRequiredService<IFinanceOpeningPeriodReader>(), provider.GetRequiredService<IFinanceCreditAccountingPeriodPortV1>(),
            provider.GetRequiredService<IFinanceTreasuryBalanceGuard>(), provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceCreditAccountEnvelopes(Envelope(provider, ErpPermissions.FinanceCreditsPropose, permissions, FinanceAction.ProposeCredit),
                Envelope(provider, ErpPermissions.FinanceCreditsApprove, permissions, FinanceAction.ApproveCredit)),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IAuditTransactionWriterV1>(),
            FinanceCreditGroupGateV1.ApprovedFieldListsV12);
    }

    private static FinanceCreditLimitServiceV1 CreateCreditLimitService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinanceCreditLimitServiceV1(provider.GetRequiredService<FinanceDbContext>(), provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(), permissions, provider.GetRequiredService<IFinanceCreditLenderPortV1>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceCreditAccountEnvelopes(Envelope(provider, ErpPermissions.FinanceCreditsPropose, permissions, FinanceAction.ProposeCredit),
                Envelope(provider, ErpPermissions.FinanceCreditsApprove, permissions, FinanceAction.ApproveCredit)),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IAuditTransactionWriterV1>());
    }

    private static ReceiptDraftService CreateReceiptDraftService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new ReceiptDraftService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IFinanceBankAccountReader>(),
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            provider.GetRequiredService<ISettingsPrerequisiteProvider>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceReceiptDraftBinding(FinanceMoneyOperations.ManualBankReceiptV1),
            Envelope(provider, FinancePermissionNames.DraftSave, permissions, FinanceAction.SaveDraft),
            provider.GetRequiredService<TimeProvider>(),
            Envelope(provider, FinancePermissionNames.Record, permissions, FinanceAction.Record),
            provider.GetRequiredService<IFinanceRecordPeriodRule>(),
            provider.GetService<IAuditTransactionWriterV1>(),
            new SettingsFinanceDocumentNumberReservationV1(provider.GetRequiredService<FinanceDbContext>(),
                provider.GetService<ISettingsDocumentNumberSeriesV1>()));
    }

    private static ReversalProposalService CreateReversalProposalService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new ReversalProposalService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IFinanceRecordPeriodRule>(),
            provider.GetRequiredService<IFinanceReversalAllocationGate>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceReversalEnvelopes(
                Envelope(provider, FinancePermissionNames.ReversePropose, permissions,
                    FinanceAction.ProposeReversal),
                Envelope(provider, FinancePermissionNames.ReverseApprove, permissions,
                    FinanceAction.ApproveReversal)),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IFinanceTreasuryBalanceGuard>(),
            provider.GetRequiredService<IAuditTransactionWriterV1>());
    }

    private static PaymentReversalProposalService CreatePaymentReversalProposalService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new PaymentReversalProposalService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IFinanceRecordPeriodRule>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinancePaymentReversalEnvelopes(
                Envelope(provider, FinancePermissionNames.PaymentReversePropose, permissions,
                    FinanceAction.ProposePaymentReversal),
                Envelope(provider, FinancePermissionNames.PaymentReverseApprove, permissions,
                    FinanceAction.ApprovePaymentReversal)),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<IAuditTransactionWriterV1>());
    }

    private static ManualBankPaymentService CreateManualBankPaymentService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new ManualBankPaymentService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            provider.GetRequiredService<IManualBankPaymentDocumentValidationPort>(),
            provider.GetRequiredService<ISettingsPrerequisiteProvider>(),
            provider.GetRequiredService<IFinanceRecordPeriodRule>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new ManualBankPaymentBinding(FinanceMoneyOperations.ManualBankPaymentV1),
            new ManualBankPaymentAuditEnvelopes(
                Envelope(provider, FinancePermissionNames.ManualBankPaymentDraftSave, permissions,
                    FinanceAction.SaveManualBankPaymentDraft),
                Envelope(provider, FinancePermissionNames.ManualBankPaymentApprove, permissions,
                    FinanceAction.ApproveManualBankPayment),
                Envelope(provider, FinancePermissionNames.ManualBankPaymentRecord, permissions,
                    FinanceAction.RecordManualBankPayment)),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IFinanceTreasuryBalanceGuard>(),
            provider.GetRequiredService<IAuditTransactionWriterV1>(),
            new SettingsFinanceDocumentNumberReservationV1(provider.GetRequiredService<FinanceDbContext>(),
                provider.GetService<ISettingsDocumentNumberSeriesV1>()));
    }

    private static ExpensePaymentService CreateExpensePaymentService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new ExpensePaymentService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IFinanceExpenseItemCatalog>(),
            provider.GetRequiredService<IFinanceTreasuryBalanceGuard>(),
            provider.GetRequiredService<ISettingsPrerequisiteProvider>(),
            provider.GetRequiredService<IFinanceRecordPeriodRule>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            // FNS-D48/FNS-D55: the expense payment's own Settings money row (approved with the manual bank payment's
            // values: TRY, scale 2, per seed company), never the manual bank payment's key.
            new ExpensePaymentBinding(FinanceMoneyOperations.ExpensePaymentV1),
            new ExpensePaymentAuditEnvelopes(
                Envelope(provider, FinancePermissionNames.ExpensePaymentDraftSave, permissions,
                    FinanceAction.SaveExpensePaymentDraft),
                Envelope(provider, FinancePermissionNames.ExpensePaymentApprove, permissions,
                    FinanceAction.ApproveExpensePayment),
                Envelope(provider, FinancePermissionNames.ExpensePaymentRecord, permissions,
                    FinanceAction.RecordExpensePayment)),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IAuditTransactionWriterV1>(),
            new SettingsFinanceDocumentNumberReservationV1(provider.GetRequiredService<FinanceDbContext>(),
                provider.GetService<ISettingsDocumentNumberSeriesV1>()));
    }

    private static CashTransferService CreateCashTransferService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new CashTransferService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IFinanceTreasuryBalanceGuard>(),
            provider.GetRequiredService<IFinanceCurrencyExchangeRateRule>(),
            provider.GetRequiredService<ISettingsPrerequisiteProvider>(),
            provider.GetRequiredService<IFinanceRecordPeriodRule>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            // FNS-D62: only the independently published transfer profile governs each leg.
            // A missing profile is unavailable; the manual bank payment profile is never a runtime fallback.
            new CashTransferBinding(FinanceMoneyOperations.CashTransferV1),
            new CashTransferAuditEnvelopes(
                Envelope(provider, FinancePermissionNames.CashTransferCommit, permissions,
                    FinanceAction.CommitCashTransfer)),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IAuditTransactionWriterV1>(),
            new SettingsFinanceDocumentNumberReservationV1(provider.GetRequiredService<FinanceDbContext>(),
                provider.GetService<ISettingsDocumentNumberSeriesV1>()));
    }

    private static FinanceAllocationService CreateAllocationService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinanceAllocationService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IAuditEventCollector>(),
            Envelope(provider, FinancePermissionNames.AllocationWrite, permissions, FinanceAction.WriteAllocation),
            provider.GetRequiredService<TimeProvider>());
    }

    private static FinancePaymentAllocationService CreatePaymentAllocationService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinancePaymentAllocationService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IAuditEventCollector>(),
            Envelope(provider, FinancePermissionNames.AllocationWrite, permissions, FinanceAction.WriteAllocation),
            provider.GetRequiredService<TimeProvider>());
    }

    private static CustomerInstrumentService CreateCustomerInstrumentService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new CustomerInstrumentService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IFinanceBankAccountReader>(),
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            provider.GetRequiredService<ISettingsPrerequisiteProvider>(),
            provider.GetRequiredService<IFinanceRecordPeriodRule>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            // Collection records a manual bank receipt, so the face value is judged by that approved profile.
            new FinanceReceiptDraftBinding(FinanceMoneyOperations.ManualBankReceiptV1),
            new CustomerInstrumentAuditEnvelopes(
                Envelope(provider, FinancePermissionNames.CustomerInstrumentReceive, permissions,
                    FinanceAction.ReceiveCustomerInstrument),
                Envelope(provider, FinancePermissionNames.CustomerInstrumentStatusChange, permissions,
                    FinanceAction.ChangeCustomerInstrumentStatus),
                Envelope(provider, FinancePermissionNames.CustomerInstrumentCollect, permissions,
                    FinanceAction.CollectCustomerInstrument)),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<IAuditTransactionWriterV1>(),
            new SettingsFinanceDocumentNumberReservationV1(provider.GetRequiredService<FinanceDbContext>(),
                provider.GetService<ISettingsDocumentNumberSeriesV1>()));
    }

    private static PartyLedgerService CreatePartyLedgerService(IServiceProvider provider) =>
        new PartyLedgerService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<FinancePermissionPort>());

    private static IFinanceCollateralTypeCatalogReader CreateCollateralTypeCatalogReader(IServiceProvider provider) =>
        provider.GetService<ISettingsCollateralTypeCatalogV1>() is { } catalog
            ? new SettingsFinanceCollateralTypeCatalogReaderV1(provider.GetRequiredService<ICompanyContext>(),
                provider.GetRequiredService<ICurrentUser>(), catalog, provider.GetRequiredService<TimeProvider>())
            : new UnavailableFinanceCollateralTypeCatalogReader();

    private static PartyLedgerReadService CreatePartyLedgerReadService(IServiceProvider provider) =>
        new PartyLedgerReadService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<FinancePermissionPort>(),
            provider.GetRequiredService<IPartyLedgerService>());

    /// <summary>The ADR-009 envelope of the request: the permission that authorizes the write and its roles.</summary>
    private static FinanceAuditEnvelope Envelope(IServiceProvider provider, string permissionCode,
        FinancePermissionPort permissions, FinanceAction action)
    {
        var correlation = provider.GetRequiredService<ICorrelationContext>();
        return new FinanceAuditEnvelope(permissionCode, permissions.RoleCodesOf(action), correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent);
    }

    /// <summary>Applies Finance's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializeFinancePersistenceAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
