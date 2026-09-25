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
/// </summary>
public sealed class FinanceShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "CHATGPT-BUSINESS/FINANCE";
    public const string OwnerContractVersion = "1.2";
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
        ];
    }

    // Finance records are one company's and have no site dimension: a single company, company-wide.
    private static ShellCapabilityDeclaration Declaration(string capabilityId, string routeId, string routePath,
        string screenId, ShellCapabilityRoutePrecedence precedence, IReadOnlyCollection<string> permissions) =>
        new ShellCapabilityDeclaration(capabilityId, routeId, routePath, screenId, precedence,
            ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide, permissions,
            ImplementationAvailable: true);
}
