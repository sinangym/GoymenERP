using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure.Shell;

/// <summary>
/// FIN-UI-1: Finance's screens in the shell — F-02 Tahsilat / Ödeme (the manual-receipt drafts) and
/// F-04 Banka Hesapları (the bank-account master), UI catalog / Master §27.11. The ids and paths are
/// the owner's technical mapping (#254 5654539215), on Cari's Turkish-path convention.
/// <para>
/// <b>Screen ids:</b> the registry needs one screen id per route. The catalog's business ids F-02
/// and F-04 name the lists; the other routes carry technical suffixes of the same screen (the Product
/// precedent U-01 / U-01-360 / U-02 / U-02-EDIT): F-02-NEW, F-02-DETAIL, F-04-NEW. The approval is a
/// control and keeps its list's F-04.
/// </para>
/// <para>
/// <b>Published (FIN-UI-1c):</b> every declaration is <c>ImplementationAvailable: true</c>, flipped by its own
/// PR after the FIN-01 browser acceptance on the staged profile (run 35444049175, #332), the S03-c precedent.
/// Nothing else changed: ids, routes, screens, permissions, context and scope are FIN-UI-1a's. The approval
/// is a control of the F-04 list (<c>ParentCapabilityId</c>), not a route. A declaration grants nothing: a
/// screen is published to an actor only with its own narrow Finance name (grants stay a human gate), and
/// each route's service measures its permission afresh.
/// </para>
/// <para>
/// The host registers this provider together with its shell authority (the central writer's); this
/// module does not.
/// </para>
/// <para>
/// <b>FI04:</b> F-03's allocation workspace remains at <c>/finans/dagitimlar</c>. The admitted open-item
/// metadata read adds its own list and detail routes under <c>finance.open-items.read</c>. Amount disclosure
/// stays an endpoint/DTO decision under <c>finance.open-items.amount.read</c>; it is not a shell route gate.
/// </para>
/// <para>
/// <b>1.7 (B-05, KLN-D3, FNS-D14):</b> the party ledger (cari ekstresi + bakiye özeti) opens from two places, both
/// Finance's read of the one open-item sub-ledger under <c>finance.party-ledger.read</c> (no new name): Muhasebe &gt;
/// Cari Hesaplar (catalog M-03, <c>/muhasebe/cari-hesaplar</c>) and Cari &gt; Cari Hesap Takibi (catalog C-04,
/// <c>/cari/hesap-takibi</c>). Neither is a new master; amounts stay the endpoint's separate
/// <c>finance.party-ledger.amount.read</c> decision, not a route gate.
/// </para>
/// <para>
/// <b>1.8 (API-23):</b> the reversal of a recorded payment is two controls of the payment detail (no new route):
/// propose under <c>finance.manual-bank-payment.reverse.propose</c> and approve / reject under
/// <c>finance.manual-bank-payment.reverse.approve</c>. The approver must be another user (service and database).
/// </para>
/// <para>
/// <b>1.9 (FNS-D25, API-25):</b> the payment not tied to a party opens at <c>/finans/masraf-odemeleri</c> under
/// <c>finance.expense-payment.draft.read</c>; preparing, approving (another user) and the final record are controls
/// of that workspace under their own narrow names. The record also needs the Accounting period read, like every
/// Finance record.
/// </para>
/// <para>
/// <b>1.10 (F-018 L1; FNS-D27 / FNS-D27a / FNS-D46; one step, FNS-D56 / FNS-D57(3)):</b> the company accounts'
/// credit limits open at <c>/finans/kredi-limitleri</c> under <c>finance.credit-limits.read</c>. Proposing (FNS-D27a)
/// and approving (a one-step set, change or removal, or the decision of another person's proposal, FNS-D27) are two
/// controls of that route under their own narrow names. The approve name is Critical in the manifest (FNS-D46), which
/// changes no route rule; no role or user receives any of these names here.
/// </para>
/// <para>
/// <b>1.11 (FNS-D24, API-32/34):</b> a transfer between two treasury accounts (same currency, or a currency exchange
/// at one rate recorded as a single transaction) opens at <c>/finans/virman</c> under
/// <c>finance.cash-transfers.read</c>; the commit is a control of that workspace under
/// <c>finance.cash-transfers.commit</c> and, like every Finance record, the Accounting period read. No role or user
/// receives either name here.
/// </para>
/// <para>
/// <b>1.12 (F-018 L2; FNS-D57 / FNS-D31 / FNS-D58, API-85b):</b> the company accounts' customer collaterals open at
/// <c>/finans/teminatlar</c> under <c>finance.collaterals.read</c>; recording, changing and releasing (one step, no
/// approval, FNS-D57) is one control of that route under <c>finance.collaterals.record</c>. Neither name is Critical and no
/// role or user receives either name here (the persona distribution is open).
/// </para>
/// </summary>
public sealed class FinanceShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "CHATGPT-BUSINESS/FINANCE";
    public const string OwnerContractVersion = "1.12";
    public const string ReceiptListScreenId = "F-02";
    public const string ReceiptDraftScreenId = "F-02-NEW";
    public const string ReceiptDetailScreenId = "F-02-DETAIL";
    public const string ReceiptReversalApprovalScreenId = "F-02-REVERSAL-APPROVAL";
    public const string BankAccountListScreenId = "F-04";
    public const string BankAccountProposeScreenId = "F-04-NEW";

    public const string ReceiptListCapabilityId = "finance.receipts.list";
    public const string ReceiptListRouteId = "finance.receipts";
    public const string ReceiptListRoutePath = "/finans/tahsilatlar";
    public const string ReceiptDraftCapabilityId = "finance.receipts.draft";
    public const string ReceiptDraftRouteId = "finance.receipts.new";
    public const string ReceiptDraftRoutePath = "/finans/tahsilatlar/yeni";
    public const string ReceiptDetailCapabilityId = "finance.receipts.detail";
    public const string ReceiptRecordCapabilityId = "finance.receipts.record";
    public const string ReceiptReverseProposeCapabilityId = "finance.receipts.reverse.propose";
    public const string ReceiptReverseApproveCapabilityId = "finance.receipts.reverse.approve";
    public const string ReceiptDetailRouteId = "finance.receipts.detail";
    public const string ReceiptDetailRoutePath = "/finans/tahsilatlar/:sourceId";
    public const string ReceiptReversalApprovalRouteId = "finance.receipts.reversal-approvals";
    public const string ReceiptReversalApprovalRoutePath = "/finans/ters-kayit-onaylari";

    public const string CustomerInstrumentListScreenId = "F-02-INSTRUMENTS";
    public const string CustomerInstrumentListCapabilityId = "finance.customer-instruments.list";
    public const string CustomerInstrumentListRouteId = "finance.customer-instruments.list";
    public const string CustomerInstrumentListRoutePath = "/finans/musteri-cek-senetleri";
    public const string OfficialInvoiceImportUploadScreenId = "F-02-OFFICIAL-INVOICE-UPLOAD";
    public const string OfficialInvoiceImportApprovalScreenId = "F-02-OFFICIAL-INVOICE-APPROVALS";
    public const string OfficialInvoiceImportUploadCapabilityId = "finance.official-invoice-import.upload";
    public const string OfficialInvoiceImportUploadRouteId = "finance.official-invoice-import.upload";
    public const string OfficialInvoiceImportUploadRoutePath = "/finans/onayli-fatura-taksit-kaynagi-yukle";
    public const string OfficialInvoiceImportApprovalCapabilityId = "finance.official-invoice-import.approve";
    public const string OfficialInvoiceImportApprovalRouteId = "finance.official-invoice-import.approve";
    public const string OfficialInvoiceImportApprovalRoutePath = "/finans/resmi-fatura-aktarim-onaylari";

    public const string BankAccountListCapabilityId = "finance.bank-accounts.list";
    public const string BankAccountListRouteId = "finance.bank-accounts";
    public const string BankAccountListRoutePath = "/finans/banka-hesaplari";
    public const string BankAccountProposeCapabilityId = "finance.bank-accounts.propose";
    public const string BankAccountProposeRouteId = "finance.bank-accounts.new";
    public const string BankAccountProposeRoutePath = "/finans/banka-hesaplari/yeni";
    public const string BankAccountApproveCapabilityId = "finance.bank-accounts.approve";

    public const string ManualBankPaymentListScreenId = "F-02-PAYMENTS";
    public const string ManualBankPaymentCreateScreenId = "F-02-PAYMENT-NEW";
    public const string ManualBankPaymentDetailScreenId = "F-02-PAYMENT-DETAIL";
    public const string ManualBankPaymentListCapabilityId = "finance.manual-bank-payments.list";
    public const string ManualBankPaymentListRouteId = "finance.manual-bank-payments.list";
    public const string ManualBankPaymentListRoutePath = "/finans/odemeler";
    public const string ManualBankPaymentCreateCapabilityId = "finance.manual-bank-payments.create";
    public const string ManualBankPaymentCreateRouteId = "finance.manual-bank-payments.create";
    public const string ManualBankPaymentCreateRoutePath = "/finans/odemeler/yeni";
    public const string ManualBankPaymentDetailCapabilityId = "finance.manual-bank-payments.detail";
    public const string ManualBankPaymentDetailRouteId = "finance.manual-bank-payments.detail";
    public const string ManualBankPaymentDetailRoutePath = "/finans/odemeler/:paymentId";
    public const string ManualBankPaymentApproveCapabilityId = "finance.manual-bank-payments.approve";
    public const string ManualBankPaymentRecordCapabilityId = "finance.manual-bank-payments.record";
    public const string ManualBankPaymentReverseProposeCapabilityId = "finance.manual-bank-payments.reverse.propose";
    public const string ManualBankPaymentReverseApproveCapabilityId = "finance.manual-bank-payments.reverse.approve";
    public const string ExpensePaymentWorkspaceScreenId = "F-25-EXPENSE-PAYMENTS";
    public const string ExpensePaymentWorkspaceCapabilityId = "finance.expense-payments.workspace";
    public const string ExpensePaymentWorkspaceRouteId = "finance.expense-payments.workspace";
    public const string ExpensePaymentWorkspaceRoutePath = "/finans/masraf-odemeleri";
    public const string ExpensePaymentDraftCapabilityId = "finance.expense-payments.draft";
    public const string ExpensePaymentApproveCapabilityId = "finance.expense-payments.approve";
    public const string ExpensePaymentRecordCapabilityId = "finance.expense-payments.record";
    public const string AllocationListScreenId = "F-03";
    public const string AllocationListCapabilityId = "finance.allocations.list";
    public const string AllocationListRouteId = "finance.allocations.list";
    public const string AllocationListRoutePath = "/finans/dagitimlar";
    public const string AllocationWriteCapabilityId = "finance.allocations.write";
    public const string OpenItemListScreenId = "F-03-OPEN-ITEMS";
    public const string OpenItemDetailScreenId = "F-03-OPEN-ITEM-DETAIL";
    public const string OpenItemListCapabilityId = "finance.open-items.list";
    public const string OpenItemListRouteId = "finance.open-items.list";
    public const string OpenItemListRoutePath = "/finans/acik-kalemler";
    public const string OpenItemDetailCapabilityId = "finance.open-items.detail";
    public const string OpenItemDetailRouteId = "finance.open-items.detail";
    public const string OpenItemDetailRoutePath = "/finans/acik-kalemler/:openItemId";

    public const string PartyLedgerAccountingScreenId = "M-03";
    public const string PartyLedgerAccountingCapabilityId = "finance.party-ledger.accounting";
    public const string PartyLedgerAccountingRouteId = "finance.party-ledger.accounting";
    public const string PartyLedgerAccountingRoutePath = "/muhasebe/cari-hesaplar";
    public const string PartyLedgerCariScreenId = "C-04";
    public const string PartyLedgerCariCapabilityId = "finance.party-ledger.cari";
    public const string PartyLedgerCariRouteId = "finance.party-ledger.cari";
    public const string PartyLedgerCariRoutePath = "/cari/hesap-takibi";
    public const string CreditLimitListScreenId = "F-18-CREDIT-LIMITS";
    public const string CreditLimitListCapabilityId = "finance.credit-limits.list";
    public const string CreditLimitListRouteId = "finance.credit-limits.list";
    public const string CreditLimitListRoutePath = "/finans/kredi-limitleri";
    public const string CreditLimitProposeCapabilityId = "finance.credit-limits.propose";
    public const string CreditLimitApproveCapabilityId = "finance.credit-limits.approve";
    public const string CollateralListScreenId = "F-18-COLLATERALS";
    public const string CollateralListCapabilityId = "finance.collaterals.list";
    public const string CollateralListRouteId = "finance.collaterals.list";
    public const string CollateralListRoutePath = "/finans/teminatlar";
    public const string CollateralRecordCapabilityId = "finance.collaterals.record";
    public const string CashTransferWorkspaceScreenId = "F-32-CASH-TRANSFERS";
    public const string CashTransferWorkspaceCapabilityId = "finance.cash-transfers.workspace";
    public const string CashTransferWorkspaceRouteId = "finance.cash-transfers.workspace";
    public const string CashTransferWorkspaceRoutePath = "/finans/virman";
    public const string CashTransferCommitCapabilityId = "finance.cash-transfers.commit";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var receiptDetail = Declaration(ReceiptDetailCapabilityId, ReceiptDetailRouteId, ReceiptDetailRoutePath,
            ReceiptDetailScreenId, ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, [FinancePermissionNames.DraftRead]);
        var bankAccounts = Declaration(BankAccountListCapabilityId, BankAccountListRouteId, BankAccountListRoutePath,
            BankAccountListScreenId, ShellCapabilityRoutePrecedence.Static, [ErpPermissions.FinanceBankAccountRead]);
        var manualBankPaymentDetail = Declaration(ManualBankPaymentDetailCapabilityId,
            ManualBankPaymentDetailRouteId, ManualBankPaymentDetailRoutePath, ManualBankPaymentDetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            [FinancePermissionNames.ManualBankPaymentDraftRead]);
        var expensePayments = Declaration(ExpensePaymentWorkspaceCapabilityId, ExpensePaymentWorkspaceRouteId,
            ExpensePaymentWorkspaceRoutePath, ExpensePaymentWorkspaceScreenId, ShellCapabilityRoutePrecedence.Static,
            [FinancePermissionNames.ExpensePaymentDraftRead]);
        var cashTransfers = Declaration(CashTransferWorkspaceCapabilityId, CashTransferWorkspaceRouteId,
            CashTransferWorkspaceRoutePath, CashTransferWorkspaceScreenId, ShellCapabilityRoutePrecedence.Static,
            [FinancePermissionNames.CashTransferRead]);
        var creditLimits = Declaration(CreditLimitListCapabilityId, CreditLimitListRouteId, CreditLimitListRoutePath,
            CreditLimitListScreenId, ShellCapabilityRoutePrecedence.Static, [ErpPermissions.FinanceCreditLimitsRead]);
        var collaterals = Declaration(CollateralListCapabilityId, CollateralListRouteId, CollateralListRoutePath,
            CollateralListScreenId, ShellCapabilityRoutePrecedence.Static, [ErpPermissions.FinanceCollateralsRead]);
        return
        [
            Declaration(ReceiptListCapabilityId, ReceiptListRouteId, ReceiptListRoutePath, ReceiptListScreenId,
                ShellCapabilityRoutePrecedence.Static, [FinancePermissionNames.DraftRead]),
            Declaration(ReceiptDraftCapabilityId, ReceiptDraftRouteId, ReceiptDraftRoutePath, ReceiptDraftScreenId,
                ShellCapabilityRoutePrecedence.Static,
                [FinancePermissionNames.DraftSave, ErpPermissions.FinanceBankAccountRead]),
            receiptDetail,
            // FIN-T05 + D1: Record is a control of a readable detail. Its money permission does not
            // imply the caller's narrow Accounting period read, and neither permission alone opens it.
            receiptDetail with
            {
                CapabilityId = ReceiptRecordCapabilityId,
                ParentCapabilityId = ReceiptDetailCapabilityId,
                RequiredPermissions =
                    [FinancePermissionNames.DraftRead, FinancePermissionNames.Record, ErpPermissions.AccountingPeriodsRead],
            },
            receiptDetail with
            {
                CapabilityId = ReceiptReverseProposeCapabilityId,
                ParentCapabilityId = ReceiptDetailCapabilityId,
                RequiredPermissions = [FinancePermissionNames.DraftRead, FinancePermissionNames.ReversePropose],
            },
            Declaration(ReceiptReverseApproveCapabilityId, ReceiptReversalApprovalRouteId,
                ReceiptReversalApprovalRoutePath, ReceiptReversalApprovalScreenId,
                ShellCapabilityRoutePrecedence.Static, [FinancePermissionNames.ReverseApprove]),
            Declaration(CustomerInstrumentListCapabilityId, CustomerInstrumentListRouteId,
                CustomerInstrumentListRoutePath, CustomerInstrumentListScreenId,
                ShellCapabilityRoutePrecedence.Static, [ErpPermissions.FinanceCustomerInstrumentRead]),
            Declaration(OfficialInvoiceImportUploadCapabilityId, OfficialInvoiceImportUploadRouteId,
                OfficialInvoiceImportUploadRoutePath, OfficialInvoiceImportUploadScreenId,
                ShellCapabilityRoutePrecedence.Static, [FinanceOfficialInvoiceImportPermissionNamesV1.Upload]),
            Declaration(OfficialInvoiceImportApprovalCapabilityId, OfficialInvoiceImportApprovalRouteId,
                OfficialInvoiceImportApprovalRoutePath, OfficialInvoiceImportApprovalScreenId,
                ShellCapabilityRoutePrecedence.Static, [FinanceOfficialInvoiceImportPermissionNamesV1.Approve]),
            bankAccounts,
            Declaration(BankAccountProposeCapabilityId, BankAccountProposeRouteId, BankAccountProposeRoutePath,
                BankAccountProposeScreenId, ShellCapabilityRoutePrecedence.Static, [ErpPermissions.FinanceBankAccountPropose]),
            // The approval is a control on the list: the list's route and access, plus the approve name.
            bankAccounts with
            {
                CapabilityId = BankAccountApproveCapabilityId,
                ParentCapabilityId = BankAccountListCapabilityId,
                RequiredPermissions = [ErpPermissions.FinanceBankAccountRead, ErpPermissions.FinanceBankAccountApprove],
            },
            Declaration(ManualBankPaymentListCapabilityId, ManualBankPaymentListRouteId,
                ManualBankPaymentListRoutePath, ManualBankPaymentListScreenId,
                ShellCapabilityRoutePrecedence.Static, [FinancePermissionNames.ManualBankPaymentDraftRead]),
            Declaration(ManualBankPaymentCreateCapabilityId, ManualBankPaymentCreateRouteId,
                ManualBankPaymentCreateRoutePath, ManualBankPaymentCreateScreenId,
                ShellCapabilityRoutePrecedence.Static, [FinancePermissionNames.ManualBankPaymentDraftSave]),
            manualBankPaymentDetail,
            manualBankPaymentDetail with
            {
                CapabilityId = ManualBankPaymentApproveCapabilityId,
                ParentCapabilityId = ManualBankPaymentDetailCapabilityId,
                RequiredPermissions =
                    [FinancePermissionNames.ManualBankPaymentDraftRead, FinancePermissionNames.ManualBankPaymentApprove],
            },
            manualBankPaymentDetail with
            {
                CapabilityId = ManualBankPaymentRecordCapabilityId,
                ParentCapabilityId = ManualBankPaymentDetailCapabilityId,
                RequiredPermissions =
                [
                    FinancePermissionNames.ManualBankPaymentDraftRead,
                    FinancePermissionNames.ManualBankPaymentRecord,
                    ErpPermissions.AccountingPeriodsRead,
                ],
            },
            manualBankPaymentDetail with
            {
                CapabilityId = ManualBankPaymentReverseProposeCapabilityId,
                ParentCapabilityId = ManualBankPaymentDetailCapabilityId,
                RequiredPermissions =
                    [FinancePermissionNames.ManualBankPaymentDraftRead, FinancePermissionNames.PaymentReversePropose],
            },
            manualBankPaymentDetail with
            {
                CapabilityId = ManualBankPaymentReverseApproveCapabilityId,
                ParentCapabilityId = ManualBankPaymentDetailCapabilityId,
                RequiredPermissions =
                [
                    FinancePermissionNames.ManualBankPaymentDraftRead,
                    FinancePermissionNames.PaymentReverseApprove,
                    ErpPermissions.AccountingPeriodsRead,
                ],
            },
            // FNS-D25: the workspace lists; each command is a control of it under its own narrow name.
            expensePayments,
            expensePayments with
            {
                CapabilityId = ExpensePaymentDraftCapabilityId,
                ParentCapabilityId = ExpensePaymentWorkspaceCapabilityId,
                RequiredPermissions =
                    [FinancePermissionNames.ExpensePaymentDraftRead, FinancePermissionNames.ExpensePaymentDraftSave],
            },
            expensePayments with
            {
                CapabilityId = ExpensePaymentApproveCapabilityId,
                ParentCapabilityId = ExpensePaymentWorkspaceCapabilityId,
                RequiredPermissions =
                    [FinancePermissionNames.ExpensePaymentDraftRead, FinancePermissionNames.ExpensePaymentApprove],
            },
            expensePayments with
            {
                CapabilityId = ExpensePaymentRecordCapabilityId,
                ParentCapabilityId = ExpensePaymentWorkspaceCapabilityId,
                RequiredPermissions =
                [
                    FinancePermissionNames.ExpensePaymentDraftRead,
                    FinancePermissionNames.ExpensePaymentRecord,
                    ErpPermissions.AccountingPeriodsRead,
                ],
            },
            Declaration(AllocationListCapabilityId, AllocationListRouteId, AllocationListRoutePath,
                AllocationListScreenId, ShellCapabilityRoutePrecedence.Static,
                [FinancePermissionNames.AllocationRead]),
            Declaration(AllocationListCapabilityId, AllocationListRouteId, AllocationListRoutePath,
                AllocationListScreenId, ShellCapabilityRoutePrecedence.Static,
                [FinancePermissionNames.AllocationRead]) with
            {
                CapabilityId = AllocationWriteCapabilityId,
                ParentCapabilityId = AllocationListCapabilityId,
                RequiredPermissions = [FinancePermissionNames.AllocationRead, FinancePermissionNames.AllocationWrite],
            },
            // FI04: route visibility is metadata-read only. Amount disclosure remains the endpoint's separate
            // finance.open-items.amount.read decision and must not become a shell/navigation gate.
            Declaration(OpenItemListCapabilityId, OpenItemListRouteId, OpenItemListRoutePath,
                OpenItemListScreenId, ShellCapabilityRoutePrecedence.Static,
                [FinancePermissionNames.OpenItemRead]),
            Declaration(OpenItemDetailCapabilityId, OpenItemDetailRouteId, OpenItemDetailRoutePath,
                OpenItemDetailScreenId, ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                [FinancePermissionNames.OpenItemRead]),
            // B-05 (KLN-D3): the same party-ledger read from Muhasebe and from Cari; amounts are the endpoint's call.
            Declaration(PartyLedgerAccountingCapabilityId, PartyLedgerAccountingRouteId, PartyLedgerAccountingRoutePath,
                PartyLedgerAccountingScreenId, ShellCapabilityRoutePrecedence.Static,
                [FinancePermissionNames.PartyLedgerRead]),
            Declaration(PartyLedgerCariCapabilityId, PartyLedgerCariRouteId, PartyLedgerCariRoutePath,
                PartyLedgerCariScreenId, ShellCapabilityRoutePrecedence.Static,
                [FinancePermissionNames.PartyLedgerRead]),
            // 1.10 (FNS-D27 / FNS-D27a; one step, FNS-D56 / FNS-D57(3)): an approver changes a limit in one step or
            // decides another person's proposal (the server and the database refuse a self-decision of a proposal).
            creditLimits,
            creditLimits with
            {
                CapabilityId = CreditLimitProposeCapabilityId,
                ParentCapabilityId = CreditLimitListCapabilityId,
                RequiredPermissions = [ErpPermissions.FinanceCreditLimitsRead, ErpPermissions.FinanceCreditLimitsPropose],
            },
            creditLimits with
            {
                CapabilityId = CreditLimitApproveCapabilityId,
                ParentCapabilityId = CreditLimitListCapabilityId,
                RequiredPermissions = [ErpPermissions.FinanceCreditLimitsRead, ErpPermissions.FinanceCreditLimitsApprove],
            },
            // 1.12 (FNS-D57): one step; recording, changing and releasing are one control under the record name.
            collaterals,
            collaterals with
            {
                CapabilityId = CollateralRecordCapabilityId,
                ParentCapabilityId = CollateralListCapabilityId,
                RequiredPermissions = [ErpPermissions.FinanceCollateralsRead, ErpPermissions.FinanceCollateralsRecord],
            },
            // 1.11 (FNS-D24): one transaction per transfer; the commit also needs the Accounting period read.
            cashTransfers,
            cashTransfers with
            {
                CapabilityId = CashTransferCommitCapabilityId,
                ParentCapabilityId = CashTransferWorkspaceCapabilityId,
                RequiredPermissions =
                [
                    FinancePermissionNames.CashTransferRead,
                    FinancePermissionNames.CashTransferCommit,
                    ErpPermissions.AccountingPeriodsRead,
                ],
            },
        ];
    }

    // Finance records are one company's and have no site dimension: a single company, company-wide.
    private static ShellCapabilityDeclaration Declaration(string capabilityId, string routeId, string routePath,
        string screenId, ShellCapabilityRoutePrecedence precedence, IReadOnlyCollection<string> permissions) =>
        new ShellCapabilityDeclaration(capabilityId, routeId, routePath, screenId, precedence,
            ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide, permissions,
            ImplementationAvailable: true);
}
