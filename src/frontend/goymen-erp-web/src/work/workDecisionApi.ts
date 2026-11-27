import { decodeExpensePaymentView, type ExpensePaymentView } from '../finance/expensePaymentContract'
import { decodePaymentReversalProposal, type PaymentReversalProposalView } from '../finance/paymentReversalContract'
import { decodeReversalProposal, type ReversalProposalView, type PosCommissionBearer } from '../finance/contract'
import { decodePriceListLine, decodePriceListScope, type PriceListLine, type PriceListScope } from '../product/priceLists/contract'
import { isPiecesPerPackage } from '../product/packageImportApi'
import { isRevisionText, type CreditLimitKind } from '../finance/CreditLimitContract'
import { isNonNegativeDecimalText, isPositiveDecimalText } from '../finance/decimalText'
import { decodeOfficialInvoice, OFFICIAL_AMOUNT_FIELDS, type OfficialInvoice } from '../finance/officialSalesInvoiceApi'
import { decodeDecisionDetail, type OfficialInvoiceDecisionDetail } from '../finance/officialInvoiceImportApprovalContract'
import { isCountedKilogramText, isPieceCount } from '../inventory/stockCountContract'
import { KG_TEXT, KG_SIGNED_TEXT, KG_DECIMAL_TEXT, isDispatchKilogramsProduct, type DispatchKilograms } from '../logistics/dispatchKilogramsContract'
import { checkTaxIdentity, type TaxIdentityKind } from '../cari/taxIdentity'
import { LEGAL_NAME_MAX, LEGAL_NAME_REASON_MAX } from '../cari/legalName'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'
import { API_BASE, devHeaders } from '../finance/http'
import { decodeBankAccountPage, type BankAccountView, isBoundUuid, isPositiveInt64Text } from '../finance/contract'
import { hasExactKeys, replayHeadersAgree } from '../finance/wireShape'
import { readJson, type CommandOutcome } from '../settings/discountLimitApi'

export const WORK_DECISION_KINDS = ['inventory.opening-cost.v1', 'finance.cash-account.v1',
  'finance.credit-account.v1', 'finance.cash-account-passivation.v1', 'finance.cash-account-reactivation.v1','sales.order-draft.v1','sales.invoice.v1','sales.subcontract-invoice.v1','purchasing.request.v1','purchasing.order.v1','purchasing.service-order.v1','purchasing.incoming-invoice-variance.v1','purchasing.goods-receipt-batch.v1','manufacturing.plan.v1','inventory.transfer-loss.v1','counterparty.tax-identity-change.v1','counterparty.legal-name-change.v1','logistics.plan-excess.v1','inventory.transfer-overage.v1','logistics.weighing-difference.v1','purchasing.supplier-return-cost-exception.v1','inventory.stock-count.v1','finance.bank-account.v1','inventory.stock-scrap.v1','logistics.dispatch-note.v1','logistics.customer-return.v1','finance.manual-bank-payment.v1','finance.bank-account-iban-change.v1','product.package-standard-version.v1','product.price-list-version.v1','finance.party-credit-limit.v1','finance.official-invoice-import.v1','finance.official-sales-invoice.v1','finance.manual-bank-payment-reversal.v1','finance.manual-receipt-reversal.v1','finance.expense-payment.v1'] as const
// Native owner purposes: kind prefixes do not define their actual capability ids.
export const COUNTERPARTY_WORK_DECISION_CAPABILITY = {
  'counterparty.tax-identity-change.v1': 'cari.tax-identity-change.work-decision',
  'counterparty.legal-name-change.v1': 'cari.legal-name-change.work-decision',
} as const
export type CounterpartyWorkDecisionKind = keyof typeof COUNTERPARTY_WORK_DECISION_CAPABILITY
export const REVERSAL_WORK_DECISION_CAPABILITY = {
  'finance.manual-bank-payment-reversal.v1': 'finance.manual-bank-payment-reversal.work-decision',
  'finance.manual-receipt-reversal.v1': 'finance.manual-receipt-reversal.work-decision',
} as const
export type WorkReversalKind = keyof typeof REVERSAL_WORK_DECISION_CAPABILITY
export const isWorkReversalKind = (kind: string): kind is WorkReversalKind => Object.hasOwn(REVERSAL_WORK_DECISION_CAPABILITY, kind)
export const EXPENSE_PAYMENT_WORK_DECISION_CAPABILITY = 'finance.expense-payment.work-decision'
export const DISPATCH_NOTE_WORK_DECISION_CAPABILITY = 'logistics.dispatch-note.work-decision'
export const BANK_IBAN_WORK_DECISION_CAPABILITY = 'finance.bank-account-iban-change.work-decision'
export const MANUAL_BANK_PAYMENT_WORK_DECISION_CAPABILITY = 'finance.manual-bank-payment.work-decision'
export const CUSTOMER_RETURN_WORK_DECISION_CAPABILITY = 'logistics.customer-return.work-decision'
export const PRODUCT_PRICE_LIST_WORK_DECISION_CAPABILITY = 'price-lists.price-list-version.work-decision'
export const PRODUCT_PACKAGE_WORK_DECISION_CAPABILITY = 'price-lists.package-standard-version.work-decision'
export type WorkDecisionKind = typeof WORK_DECISION_KINDS[number] | CounterpartyWorkDecisionKind | 'logistics.customer-return.v1'
export const isCounterpartyWorkDecisionKind = (kind: string): kind is CounterpartyWorkDecisionKind =>
  Object.hasOwn(COUNTERPARTY_WORK_DECISION_CAPABILITY, kind)
export const isSalesWorkDecisionKind = (kind: WorkDecisionKind) => kind === 'sales.order-draft.v1' || kind === 'sales.invoice.v1' || kind === 'sales.subcontract-invoice.v1'
/** Native owner reason/note bounds; Counterparty notes and Sales rejection reasons are optional. */
export const workDecisionReasonLimit = (kind: WorkDecisionKind) => kind === 'finance.official-sales-invoice.v1' || isWorkReversalKind(kind) ? 1000 : kind === 'logistics.customer-return.v1' ? undefined : kind === 'inventory.stock-scrap.v1' || kind === 'inventory.stock-count.v1' || isCounterpartyWorkDecisionKind(kind) ? 1000 : kind === 'manufacturing.plan.v1' ? 256 : kind === 'purchasing.incoming-invoice-variance.v1' ? 2000 : kind === 'purchasing.service-order.v1' ? 1000 : isSalesWorkDecisionKind(kind) || kind === 'purchasing.request.v1' || kind === 'product.package-standard-version.v1' ? 4000 : 500
export const isWorkDecisionKind = (value: string): value is WorkDecisionKind => value === 'product.package-standard-version.v1' || value === 'logistics.customer-return.v1' || value === 'logistics.dispatch-note.v1' || isCounterpartyWorkDecisionKind(value) || WORK_DECISION_KINDS.some(kind => kind === value)
export interface WorkDecisionRequest { readonly productPackageStandard?: { readonly effectiveFrom: string }; readonly logisticsCustomerReturn?: { readonly inventoryMovementId: string }; readonly expectedVersion: string; readonly approve: boolean; readonly reason: string | null; readonly delegationId: string | null; readonly supplierReturnCostException?: { readonly expectedPurchaseOrderVersion?: string }; readonly inventoryTransferOverage?: { readonly requestNumber: number; readonly decidedAtUtc: string }; readonly logisticsPlanExcess?: { readonly expectedInstructionVersion: string }; readonly inventoryTransferLoss?: { readonly requestNumber: number; readonly decidedAtUtc: string }; readonly manufacturingPlanReapproval?: { readonly selectedLabelIds?: readonly string[] | null } | null }
export interface CounterpartyTaxIdentityProof {
  readonly partyId: string; readonly changeRequestId: string; readonly currentTaxIdentityNumber: string
  readonly proposedTaxIdentityNumber: string; readonly proposedKind: TaxIdentityKind; readonly reason: string
}
export interface CounterpartyLegalNameProof {
  readonly partyId: string; readonly changeRequestId: string; readonly currentLegalName: string
  readonly proposedLegalName: string; readonly reason: string
}
export interface LogisticsPlanExcessProof {
  readonly instructionVersion: string; readonly instructionId: string; readonly lineId: string; readonly quantity: string
  readonly reason: string; readonly proposedBy: string; readonly proposedAtUtc: string
}
export interface InventoryTransferOverageProof {
  readonly requestNumber: number; readonly requestedAtUtc: string; readonly transferId: string; readonly lineId: string
  readonly kilograms: string; readonly pieceCount: number | null; readonly reason: string; readonly requestedBy: string; readonly receivedBy: string
}
export type LogisticsWeighingDifferenceProof = Pick<DispatchKilograms, 'dispatchNoteId' | 'instructionId' | 'theoreticalKg' | 'scaleKg' | 'differenceKg' | 'bandLevel'
  | 'warningBandPercent' | 'approvalBandPercent' | 'blockBandPercent' | 'totalExcessKg' | 'approvalRatePercent' | 'decision' | 'products' | 'enteredBy'
  | 'enteredByComplete' | 'recordedBy' | 'recordedAtUtc'>
export interface SupplierReturnCostExceptionProof {
  readonly purchaseOrderId: string; readonly purchaseOrderLineId: string; readonly goodsAcceptanceId: string; readonly inventoryMovementId: string
  readonly sourceBlock: 'Free' | 'Blocked'; readonly quantityKg: string; readonly replacementExpected: boolean; readonly reason: string
  readonly occurredAtUtc: string; readonly requestedBy: string; readonly requestedAtUtc: string
  readonly recordedExpectedPurchaseOrderVersion: string | null; readonly currentPurchaseOrderVersion: string | null; readonly undoQuantity: string | null
  readonly priceDisclosure: 'Visible' | 'Withheld'; readonly unitCostTryPerKg?: string
}
export interface InventoryStockCountProof {
  readonly labelId: string; readonly labelVersion: string; readonly ownerKind: 'Company' | 'Counterparty'; readonly ownerId: string
  readonly custodianKind: 'Company' | 'Counterparty'; readonly custodianId: string; readonly locationId: string; readonly productId: string
  readonly block: 'Free' | 'Blocked'; readonly bookKg: string; readonly countedKg: string; readonly differenceKg: string
  readonly countedAtUtc: string; readonly reason: string; readonly recordedBy: string; readonly recordedAtUtc: string
  readonly bookPieceCount: number | null; readonly countedPieceCount: number | null
}
export interface InventoryStockScrapProof extends Omit<InventoryStockCountProof, 'countedKg' | 'differenceKg' | 'countedAtUtc' | 'countedPieceCount'> {
  readonly scrapKg: string; readonly remainingKg: string; readonly scrappedAtUtc: string; readonly scrapPieceCount: number | null
}
export interface WorkBankAccountIbanChangeProof {
  readonly accountId: string; readonly companyId: string; readonly companyLegalName: string
  readonly currentIban: string | null; readonly proposedIban: string; readonly reason: string; readonly proposedBy: string; readonly proposedAtUtc: string
}
export interface WorkBankAccountProof { readonly account: BankAccountView; readonly proposedBy: string; readonly proposedAtUtc: string }
export interface WorkPartyCreditLimitPreview {
  readonly partyId: string; readonly companyAccountId: string; readonly kind: CreditLimitKind
  readonly currencyId: string | null; readonly amount: string | null; readonly baseRevision: string; readonly currentRevision: string
  readonly proposedBy: string; readonly proposedAtUtc: string
}
export interface LogisticsDispatchNoteProof {
  readonly instructionId: string; readonly dispatchNoteId: string; readonly customerPartyId: string; readonly companyAccountId: string
  readonly effectiveOn: string; readonly status: 'Draft' | 'Approved' | 'Cancelled'; readonly number: string | null; readonly eDispatchNumber: string | null
  readonly createdBy: string; readonly createdAtUtc: string; readonly lines: readonly LogisticsDispatchNoteLine[]
}
export interface LogisticsDispatchNoteLine {
  readonly lineNo: number; readonly shipmentId: string; readonly instructionLineId: string; readonly sourceOrderId: string; readonly sourceOrderLineId: string
  readonly productId: string; readonly unitId: string; readonly quantity: string; readonly quantityKg?: string
}
export interface CustomerReturnWeighingProof {
  readonly weighingId: string; readonly direction: 'TareFirst' | 'GrossFirst'; readonly firstWeighingKg: string; readonly secondWeighingKg: string
  readonly netKg: string; readonly recordedBy: string; readonly recordedAtUtc: string
}
export interface LogisticsCustomerReturnProof {
  readonly weighing?: CustomerReturnWeighingProof
  readonly loadingInstructionId: string; readonly loadingInstructionLineId: string; readonly sourceDeliveryId: string; readonly sourceShipmentId: string
  readonly sourceOrderId: string; readonly sourceOrderLineId: string; readonly unitId: string; readonly quantityUnit: 'Kilogram' | 'Piece'
  readonly quantity: string; readonly sourceDeliveredQuantity: string; readonly reason: string; readonly requestedBy: string
  readonly occurredAtUtc: string; readonly requestedAtUtc: string; readonly status: 'Pending' | 'Approved' | 'Rejected'
}
export interface WorkProductPackageStandardProof {
  readonly sourceKind: 'Import' | 'Edit'; readonly sourceLabel: string | null; readonly fileName: string | null
  readonly reason: string; readonly preparedBy: string; readonly preparedAtUtc: string
  readonly entryCount: number; readonly customerEntryCount: number
  readonly entries: readonly { readonly productId: string; readonly customerId: string | null; readonly piecesPerPackage: number }[]
}
export interface WorkProductPriceListPreview {
  readonly priceListId: string; readonly name: string; readonly currencyCode: string; readonly channel: string; readonly vatBasis: string
  readonly versionId: string; readonly versionNo: number; readonly validFrom: string; readonly validThrough: string | null
  readonly lines: readonly PriceListLine[]; readonly scope: PriceListScope
}
export interface WorkPaymentReversalPreview {
  readonly companyId: string; readonly proposal: PaymentReversalProposalView; readonly paymentDocumentNumber: string | null
  readonly partyId: string; readonly companyAccountId: string; readonly treasuryAccountId: string; readonly currencyId: string
  readonly amount: string; readonly paymentOccurredOn: string; readonly paymentValueOn: string | null; readonly originalEventId: string
  readonly paymentRecordedBy: string; readonly paymentRecordedAtUtc: string; readonly recordedFactsDigest: string; readonly treasuryAccountLabel: string
}
export interface WorkReceiptReversalOriginal {
  readonly sourceId: string; readonly sourceVersion: string; readonly documentNumber: string | null
  readonly partyId: string; readonly companyAccountId: string; readonly treasuryAccountId: string; readonly currencyId: string
  readonly amountText: string; readonly occurredOn: string; readonly valueOn: string | null
  readonly posCommissionAmountText: string | null; readonly posBankNetAmountText: string | null; readonly posCommissionBearer: PosCommissionBearer | null
  readonly originalEventId: string; readonly recordedBy: string; readonly recordedAtUtc: string
}
export interface WorkReceiptReversalPreview {
  readonly companyId: string; readonly proposal: ReversalProposalView; readonly originalReceipt: WorkReceiptReversalOriginal; readonly treasuryAccountLabel: string
}
export interface WorkExpensePaymentPreview {
  readonly payment: ExpensePaymentView; readonly treasuryAccountLabel: string; readonly revisers: readonly string[]; readonly draftFactsDigest: string
}
export interface WorkDecisionPreview {
  readonly expensePayment?: WorkExpensePaymentPreview
  readonly manualBankPaymentReversal?: WorkPaymentReversalPreview
  readonly manualReceiptReversal?: WorkReceiptReversalPreview
  readonly officialSalesInvoice?: OfficialInvoice
  readonly officialInvoiceImport?: OfficialInvoiceDecisionDetail
  readonly partyCreditLimit?: WorkPartyCreditLimitPreview
  readonly productPriceList?: WorkProductPriceListPreview
  readonly productPackageStandard?: WorkProductPackageStandardProof
  readonly logisticsCustomerReturn?: LogisticsCustomerReturnProof
  readonly approvalSupported?: boolean
  readonly logisticsDispatchNote?: LogisticsDispatchNoteProof
  readonly inventoryStockScrap?: InventoryStockScrapProof
  readonly bankAccountIbanChange?: WorkBankAccountIbanChangeProof
  readonly bankAccount?: WorkBankAccountProof
  readonly inventoryStockCount?: InventoryStockCountProof
  readonly supplierReturnCostException?: SupplierReturnCostExceptionProof
  readonly logisticsWeighingDifference?: LogisticsWeighingDifferenceProof
  readonly inventoryTransferOverage?: InventoryTransferOverageProof
  readonly logisticsPlanExcess?: LogisticsPlanExcessProof
  readonly counterpartyTaxIdentityChange?: CounterpartyTaxIdentityProof
  readonly counterpartyLegalNameChange?: CounterpartyLegalNameProof
  readonly companyId: string; readonly kind: WorkDecisionKind; readonly itemId: string; readonly documentLabel: string
  readonly sourceVersion: string; readonly pending: boolean; readonly approvalReasonRequired: boolean
  readonly inventoryTransferLoss?: { readonly requestNumber: number; readonly requestedAtUtc: string }
  readonly manufacturingPlanReapproval?: { readonly parentRollIds: readonly string[] }
  readonly rejectionSupported?: boolean
  readonly references: readonly { readonly role: string; readonly label: string; readonly recordId: string | null; readonly link: string | null }[]
  readonly rules: readonly { readonly ruleCode: string; readonly threshold: string | null; readonly actual: string | null; readonly deviation: string | null; readonly unit: string | null }[]
}
export interface WorkDecisionResult { readonly companyId: string; readonly kind: WorkDecisionKind; readonly itemId: string; readonly state: string; readonly replayed: boolean; readonly originalCorrelationId: string }
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const nullableText = (value: unknown) => value === null || typeof value === 'string'
const same = (value: unknown, expected: string) => typeof value === 'string' && value.toLowerCase() === expected.toLowerCase()
const path = (kind: WorkDecisionKind, id: string) => `/work/decisions/${encodeURIComponent(kind)}/${encodeURIComponent(id)}`
// Dispatch native Guid/Int64 parsers require the entire string, including its final character.
const dispatchGuid = (value: unknown): value is string => typeof value === 'string' && value.length === 36 && isBoundUuid(value)
const dispatchVersion = (value: unknown): value is string => typeof value === 'string' && value === value.trim() && isPositiveInt64Text(value)
const creditLimitRevision = (value: unknown): value is string => typeof value === 'string' && value === value.trim() && isRevisionText(value)
const creditLimitAmount = (value: unknown): value is string => {
  return typeof value === 'string' && value.length <= 64 && exactNativeClrAmount(value, true)
}
const exactNativeClrAmount = (value: unknown, positive: boolean): value is string => {
  if (typeof value !== 'string' || value !== value.trim() || nativeControls(value)
    || !(positive ? isPositiveDecimalText(value) : isNonNegativeDecimalText(value))) return false
  // Native credit-limit IsExactAmount and official-invoice Amount require lossless CLR decimal representation.
  // Strip only representational zeros for validation; return/display the original wire text unchanged.
  const [whole, fraction = ''] = value.split('.')
  const significantFraction = fraction.replace(/0+$/, '')
  const coefficient = (whole! + significantFraction).replace(/^0+/, '')
  const maximumCoefficient = '79228162514264337593543950335'
  return significantFraction.length <= 28 && (coefficient.length < maximumCoefficient.length
    || coefficient.length === maximumCoefficient.length && coefficient <= maximumCoefficient)
}
const requestNumber = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value > 0 && value <= 2147483647
const positiveInt32Text = (value: unknown): value is string => typeof value === 'string' && /^[1-9][0-9]*$/.test(value)
  && (value.length < 10 || value.length === 10 && value <= '2147483647')
const nativeControls = (value: string) => Array.from(value).some(character => { const code = character.charCodeAt(0); return code <= 0x1f || code >= 0x7f && code <= 0x9f })
/** Native overage decision NormalizeText: trim, required 500 characters, no embedded controls. */
export const isWorkTransferOverageReason = (value: unknown): value is string => typeof value === 'string'
  && value.trim().length > 0 && value.trim().length <= 500 && !nativeControls(value.trim())
// Native decimal(18,3) rendered as 0.###; no quantity calculation or conversion.
const canonicalPositiveQuantity = (value: unknown): value is string => typeof value === 'string' && /^(?:0|[1-9]\d{0,14})(?:\.\d{0,2}[1-9])?$/.test(value) && value !== '0'
/** Native supplier-return wire permits LF/CR/TAB, but no other control characters. */
const supplierControls = (value: string) => Array.from(value).some(character => character !== '\n' && character !== '\r' && character !== '\t' && nativeControls(character))
/** Native stock-count decision bounds: both decisions require a trimmed, nonempty note of at most 1000 characters. */
export const isWorkStockCountReason = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0 && value.trim().length <= 1000
export const isWorkSupplierReturnReason = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
  && value.length <= 500 && !supplierControls(value)
export const isWorkSupplierReturnVersion = (value: unknown): value is string => isPositiveInt64Text(value) && value !== '9223372036854775807'
const positiveSupplierUnitCost = (value: unknown): value is string => typeof value === 'string' && /^(?:0|[1-9]\d{0,13})(?:\.\d{0,3}[1-9])?$/.test(value) && value !== '0'
/** Same user-local conversion and native UTC validator as TransferReceiptPanel; never called by retry. */
export const workDecisionLocalUtc = (value: string): string | null => {
  const date = value === '' ? null : new Date(value)
  const utc = date !== null && Number.isFinite(date.getTime()) ? date.toISOString() : null
  return transferDispatchUtc(utc) ? utc : null
}
const labelIds = (value: unknown): value is readonly string[] => Array.isArray(value) && value.length > 0
  && value.every(isBoundUuid) && new Set(value.map(id => id.toLowerCase())).size === value.length
/** Exact native DateOnly representation; the owner decides business-day eligibility. */
export const isWorkPackageEffectiveDate = (value: unknown): value is string => typeof value === 'string' && value.length === 10
  && /^\d{4}-\d{2}-\d{2}$/.test(value) && value >= '0001-01-01' && value <= '9999-12-31'
  && Number.isFinite(Date.parse(value + 'T00:00:00Z')) && new Date(value + 'T00:00:00Z').toISOString().slice(0,10) === value
export const isWorkDecisionRequest = (value: unknown, kind?: WorkDecisionKind): value is WorkDecisionRequest => {
  if (!record(value) || !hasExactKeys(value, ['expectedVersion', 'approve', 'reason', 'delegationId'], kind === 'product.package-standard-version.v1' ? ['productPackageStandard'] : kind === 'logistics.customer-return.v1' ? ['logisticsCustomerReturn'] : kind === 'purchasing.supplier-return-cost-exception.v1' ? ['supplierReturnCostException'] : kind === 'inventory.transfer-overage.v1' ? ['inventoryTransferOverage'] : kind === 'logistics.plan-excess.v1' ? ['logisticsPlanExcess'] : kind === 'inventory.transfer-loss.v1' ? ['inventoryTransferLoss'] : kind === 'manufacturing.plan.v1' ? ['manufacturingPlanReapproval'] : [])
    || !text(value.expectedVersion) || (kind === 'inventory.transfer-overage.v1' || kind === 'logistics.plan-excess.v1' || kind === 'manufacturing.plan.v1' || kind === 'inventory.transfer-loss.v1' || kind !== undefined && isCounterpartyWorkDecisionKind(kind)) && !isPositiveInt64Text(value.expectedVersion) || typeof value.approve !== 'boolean' || !nullableText(value.reason)
    || value.delegationId !== null && !isBoundUuid(value.delegationId)) return false
  if (kind === 'finance.expense-payment.v1') return value.approve && value.reason === null && dispatchVersion(value.expectedVersion) && value.expectedVersion !== '9223372036854775807'
    && (value.delegationId === null || dispatchGuid(value.delegationId))
  if ((kind !== undefined && isWorkReversalKind(kind)) || kind === 'finance.official-sales-invoice.v1') return value.expectedVersion === '1'
    && (value.delegationId === null || dispatchGuid(value.delegationId))
    && (value.approve ? value.reason === null : typeof value.reason === 'string' && value.reason === value.reason.trim() && value.reason.length > 0 && value.reason.length <= 1000)
  if (kind === 'finance.official-invoice-import.v1') return value.approve && value.reason === null && value.expectedVersion === '1'
    && (value.delegationId === null || dispatchGuid(value.delegationId))
  if (kind === 'product.price-list-version.v1') return value.approve && value.reason === null && dispatchVersion(value.expectedVersion) && value.expectedVersion !== '9223372036854775807' && (value.delegationId === null || dispatchGuid(value.delegationId))
  if (kind === 'product.package-standard-version.v1') {
    const selected = value.productPackageStandard
    return dispatchVersion(value.expectedVersion) && (value.delegationId === null || dispatchGuid(value.delegationId)) && typeof value.reason === 'string' && value.reason.trim().length > 0 && value.reason.trim().length <= 4000
      && (value.approve ? record(selected) && hasExactKeys(selected, ['effectiveFrom']) && isWorkPackageEffectiveDate(selected.effectiveFrom) : selected === undefined)
  }
  if (kind === 'logistics.customer-return.v1') {
    const selected=value.logisticsCustomerReturn
    return dispatchVersion(value.expectedVersion) && value.expectedVersion !== '9223372036854775807' && (value.delegationId === null || dispatchGuid(value.delegationId))
      && (value.approve ? record(selected) && hasExactKeys(selected,['inventoryMovementId']) && dispatchGuid(selected.inventoryMovementId) : selected === undefined)
  }
  if (kind === 'logistics.dispatch-note.v1') return value.approve && value.reason === null && dispatchVersion(value.expectedVersion) && value.expectedVersion !== '9223372036854775807' && (value.delegationId === null || dispatchGuid(value.delegationId))
  if (kind === 'finance.bank-account-iban-change.v1') return value.approve && value.reason === null && dispatchVersion(value.expectedVersion) && value.expectedVersion !== '9223372036854775807' && (value.delegationId === null || dispatchGuid(value.delegationId))
  if (kind === 'finance.manual-bank-payment.v1') return value.approve && value.reason === null && dispatchVersion(value.expectedVersion) && value.expectedVersion !== '9223372036854775807'
  if (kind === 'finance.bank-account.v1') return value.approve && value.reason === null && isPositiveInt64Text(value.expectedVersion) && value.expectedVersion !== '9223372036854775807'
  if (kind === 'finance.party-credit-limit.v1') return dispatchVersion(value.expectedVersion) && value.expectedVersion !== '9223372036854775807'
    && (value.approve ? value.reason === null : typeof value.reason === 'string' && value.reason === value.reason.trim() && value.reason.length > 0 && value.reason.length <= 500)
  if (kind === 'inventory.stock-count.v1' || kind === 'inventory.stock-scrap.v1') return value.expectedVersion === '1' && isWorkStockCountReason(value.reason)
  if (kind === 'purchasing.supplier-return-cost-exception.v1') {
    const selected = value.supplierReturnCostException
    return isWorkSupplierReturnVersion(value.expectedVersion) && (value.approve ? value.reason === null : isWorkSupplierReturnReason(value.reason))
      && (selected === undefined || value.approve && record(selected) && hasExactKeys(selected, ['expectedPurchaseOrderVersion']) && isWorkSupplierReturnVersion(selected.expectedPurchaseOrderVersion))
  }
  if (kind === 'logistics.weighing-difference.v1') return value.approve && value.reason === null && positiveInt32Text(value.expectedVersion)
  if (kind === 'inventory.transfer-overage.v1') {
    const selected = value.inventoryTransferOverage
    return record(selected) && hasExactKeys(selected, ['requestNumber', 'decidedAtUtc']) && requestNumber(selected.requestNumber)
      && transferDispatchUtc(selected.decidedAtUtc) && isWorkTransferOverageReason(value.reason)
  }
  if (kind === 'logistics.plan-excess.v1') {
    const selected = value.logisticsPlanExcess
    return value.approve && value.reason === null && record(selected) && hasExactKeys(selected, ['expectedInstructionVersion'])
      && isPositiveInt64Text(selected.expectedInstructionVersion)
  }
  if (kind !== undefined && isCounterpartyWorkDecisionKind(kind) && typeof value.reason === 'string' && value.reason.trim().length > 1000) return false
  if (kind === 'inventory.transfer-loss.v1') {
    const loss = value.inventoryTransferLoss
    return record(loss) && hasExactKeys(loss, ['requestNumber', 'decidedAtUtc']) && requestNumber(loss.requestNumber)
      && transferDispatchUtc(loss.decidedAtUtc) && typeof value.reason === 'string' && value.reason.trim().length > 0 && value.reason.length <= 500
  }
  const selection = value.manufacturingPlanReapproval
  return selection === undefined || selection === null || value.approve && record(selection)
    && hasExactKeys(selection, [], ['selectedLabelIds'])
    && (selection.selectedLabelIds === undefined || selection.selectedLabelIds === null || labelIds(selection.selectedLabelIds))
}
export function decodeWorkDecisionPreview(value: unknown, company: string, kind: WorkDecisionKind, id: string): WorkDecisionPreview | null {
  if (!record(value) || !hasExactKeys(value, ['companyId', 'kind', 'itemId', 'documentLabel', 'sourceVersion', 'pending', 'approvalReasonRequired', 'references', 'rules'], kind === 'finance.expense-payment.v1' ? ['rejectionSupported','expensePayment'] : kind === 'finance.manual-bank-payment-reversal.v1' ? ['rejectionSupported','manualBankPaymentReversal'] : kind === 'finance.manual-receipt-reversal.v1' ? ['rejectionSupported','manualReceiptReversal'] : kind === 'finance.official-sales-invoice.v1' ? ['rejectionSupported','officialSalesInvoice'] : kind === 'finance.official-invoice-import.v1' ? ['rejectionSupported','officialInvoiceImport'] : kind === 'finance.party-credit-limit.v1' ? ['rejectionSupported','partyCreditLimit'] : kind === 'product.price-list-version.v1' ? ['rejectionSupported','productPriceList'] : kind === 'product.package-standard-version.v1' ? ['rejectionSupported','productPackageStandard'] : kind === 'logistics.customer-return.v1' ? ['rejectionSupported','approvalSupported','logisticsCustomerReturn'] : kind === 'logistics.dispatch-note.v1' ? ['rejectionSupported', 'logisticsDispatchNote'] : kind === 'inventory.stock-scrap.v1' ? ['rejectionSupported', 'inventoryStockScrap'] : kind === 'finance.bank-account-iban-change.v1' ? ['rejectionSupported', 'bankAccountIbanChange'] : kind === 'finance.bank-account.v1' ? ['rejectionSupported', 'bankAccount'] : kind === 'inventory.stock-count.v1' ? ['rejectionSupported', 'inventoryStockCount'] : kind === 'purchasing.supplier-return-cost-exception.v1' ? ['rejectionSupported', 'supplierReturnCostException'] : kind === 'logistics.weighing-difference.v1' ? ['rejectionSupported', 'logisticsWeighingDifference'] : kind === 'inventory.transfer-overage.v1' ? ['rejectionSupported', 'inventoryTransferOverage'] : kind === 'logistics.plan-excess.v1' ? ['rejectionSupported', 'logisticsPlanExcess'] : kind === 'inventory.transfer-loss.v1' ? ['rejectionSupported', 'inventoryTransferLoss'] : kind === 'manufacturing.plan.v1' ? ['rejectionSupported', 'manufacturingPlanReapproval'] : kind === 'counterparty.tax-identity-change.v1' ? ['rejectionSupported', 'counterpartyTaxIdentityChange'] : kind === 'counterparty.legal-name-change.v1' ? ['rejectionSupported', 'counterpartyLegalNameChange'] : ['rejectionSupported'])
    || !same(value.companyId, company) || value.kind !== kind || !same(value.itemId, id) || !text(value.documentLabel)
    || (kind === 'inventory.opening-cost.v1' ? value.sourceVersion !== `submission/${id}` : !isPositiveInt64Text(value.sourceVersion))
    || typeof value.pending !== 'boolean' || value.approvalReasonRequired !== (kind === 'product.package-standard-version.v1' || kind === 'inventory.stock-scrap.v1' || kind === 'inventory.stock-count.v1' || kind === 'inventory.transfer-overage.v1' || kind === 'inventory.transfer-loss.v1' || kind === 'inventory.opening-cost.v1' || kind === 'purchasing.service-order.v1' || kind === 'purchasing.incoming-invoice-variance.v1')
    || (kind === 'manufacturing.plan.v1' ? typeof value.rejectionSupported !== 'boolean' : isWorkReversalKind(kind) || kind === 'finance.official-sales-invoice.v1' || kind === 'finance.party-credit-limit.v1' || kind === 'product.package-standard-version.v1' || kind === 'logistics.customer-return.v1' || kind === 'inventory.stock-scrap.v1' || kind === 'inventory.stock-count.v1' || kind === 'purchasing.supplier-return-cost-exception.v1' || kind === 'purchasing.goods-receipt-batch.v1' || kind === 'inventory.transfer-overage.v1' || kind === 'inventory.transfer-loss.v1' || isCounterpartyWorkDecisionKind(kind) ? value.rejectionSupported !== true : kind === 'finance.expense-payment.v1' || kind === 'finance.official-invoice-import.v1' || kind === 'product.price-list-version.v1' || kind === 'finance.bank-account-iban-change.v1' || kind === 'finance.manual-bank-payment.v1' || kind === 'logistics.dispatch-note.v1' || kind === 'finance.bank-account.v1' || kind === 'logistics.weighing-difference.v1' || kind === 'logistics.plan-excess.v1' || kind === 'purchasing.order.v1' || kind === 'purchasing.service-order.v1' || kind === 'purchasing.incoming-invoice-variance.v1' ? value.rejectionSupported !== false : value.rejectionSupported !== undefined && value.rejectionSupported !== true)
    || !Array.isArray(value.references) || value.references.length > 12 || !value.references.every(ref => record(ref)
      && hasExactKeys(ref, ['role', 'label', 'recordId', 'link']) && text(ref.role) && text(ref.label)
      && (ref.recordId === null || isBoundUuid(ref.recordId)) && nullableText(ref.link))
    || !Array.isArray(value.rules) || value.rules.length > 8 || !value.rules.every(rule => record(rule)
      && hasExactKeys(rule, ['ruleCode', 'threshold', 'actual', 'deviation', 'unit']) && text(rule.ruleCode)
      && [rule.threshold, rule.actual, rule.deviation, rule.unit].every(nullableText))) return null
  if (kind === 'product.price-list-version.v1') {
    const table = value.productPriceList
    if (!dispatchGuid(value.companyId) || !dispatchGuid(value.itemId) || !dispatchVersion(value.sourceVersion) || value.pending && value.sourceVersion === '9223372036854775807'
      || !record(table) || !hasExactKeys(table, ['priceListId','name','currencyCode','channel','vatBasis','versionId','versionNo','validFrom','validThrough','lines','scope'])
      || !dispatchGuid(table.priceListId) || !same(table.versionId,id) || !dispatchGuid(table.versionId) || !requestNumber(table.versionNo)
      || !text(table.name) || !text(table.currencyCode) || table.channel !== 'Sales' || table.vatBasis !== 'ExcludingVat'
      || !isWorkPackageEffectiveDate(table.validFrom) || table.validThrough !== null && !isWorkPackageEffectiveDate(table.validThrough)
      || table.validThrough !== null && table.validThrough < table.validFrom || !Array.isArray(table.lines) || table.lines.length === 0
      || !record(table.scope) || !hasExactKeys(table.scope,['kind','partyId','companyAccountId']) || decodePriceListScope(table.scope) === null
      || table.scope.kind === 'Customer' && (!dispatchGuid(table.scope.partyId) || !dispatchGuid(table.scope.companyAccountId))) return null
    const ids = new Set<string>(); const numbers = new Set<number>()
    for (const line of table.lines) {
      if (!record(line) || !hasExactKeys(line,['lineId','lineNo','productId','productCode','productName','priceUnitId','unitPrice','floorUnitPrice','lowerQuantity','upperQuantity'])
        // Reject noncanonical text before the native decoder's exact BigInt quantity comparison.
        || [line.unitPrice,line.floorUnitPrice,line.lowerQuantity,line.upperQuantity].some(v=>typeof v==='string' && v!==v.trim())
        || decodePriceListLine(line) === null || !dispatchGuid(line.lineId) || !dispatchGuid(line.productId) || !dispatchGuid(line.priceUnitId)
        || !requestNumber(line.lineNo)
        || ids.has(line.lineId.toLowerCase()) || numbers.has(line.lineNo)) return null
      ids.add(line.lineId.toLowerCase()); numbers.add(line.lineNo)
    }
  }
  if (kind === 'product.package-standard-version.v1') {
    const table = value.productPackageStandard
    if (!dispatchGuid(value.companyId) || !dispatchGuid(value.itemId) || !dispatchVersion(value.sourceVersion)
      || value.documentLabel !== `v${value.sourceVersion}` || !record(table)
      || !hasExactKeys(table, ['sourceKind','sourceLabel','fileName','reason','preparedBy','preparedAtUtc','entryCount','customerEntryCount','entries'])
      || table.sourceKind !== 'Import' && table.sourceKind !== 'Edit'
      || (table.sourceKind === 'Import' ? !text(table.sourceLabel) || table.sourceLabel.length > 200 || !text(table.fileName) || table.fileName.length > 255 : table.sourceLabel !== null || table.fileName !== null)
      || !text(table.reason) || table.reason.length > 4000 || !text(table.preparedBy) || table.preparedBy.length > 128 || !transferDispatchUtc(table.preparedAtUtc) || table.preparedAtUtc !== table.preparedAtUtc.trim()
      || typeof table.entryCount !== 'number' || !Number.isInteger(table.entryCount) || table.entryCount < 0 || table.entryCount > 5000
      || typeof table.customerEntryCount !== 'number' || !Number.isInteger(table.customerEntryCount) || table.customerEntryCount < 0 || table.customerEntryCount > 5000
      || !Array.isArray(table.entries) || table.entries.length !== table.entryCount + table.customerEntryCount) return null
    // Validate complete owner evidence; these counts are never recomputed for display or approval.
    const identities = new Set<string>(); let products = 0; let customers = 0
    for (const entry of table.entries) {
      if (!record(entry) || !hasExactKeys(entry, ['productId','customerId','piecesPerPackage']) || !dispatchGuid(entry.productId)
        || entry.customerId !== null && !dispatchGuid(entry.customerId) || !isPiecesPerPackage(entry.piecesPerPackage)) return null
      const identity = `${entry.productId.toLowerCase()}:${entry.customerId === null ? '' : entry.customerId.toLowerCase()}`
      if (identities.has(identity)) return null
      identities.add(identity)
      if (entry.customerId === null) products++; else customers++
    }
    if (products !== table.entryCount || customers !== table.customerEntryCount) return null
  }
  if (kind === 'finance.manual-bank-payment.v1' && (!dispatchVersion(value.sourceVersion) || value.sourceVersion === '9223372036854775807')) return null
  if (kind === 'logistics.customer-return.v1') {
    const facts=value.logisticsCustomerReturn
    const quantity=(v: unknown): v is string => typeof v==='string' && v===v.trim() && /^(?:0|[1-9]\d{0,14})(?:\.\d{1,3})?$/.test(v) && /[1-9]/.test(v)
    const utc=(v: unknown): v is string => transferDispatchUtc(v) && v===v.trim()
    // Compare representations only; never convert units or derive a returned quantity.
    const quantityOrder=(v: string)=>{const [whole,fraction='']=v.split('.');return whole!.padStart(15,'0')+'.'+fraction.padEnd(3,'0')}
    const weighing=record(facts)?facts.weighing:undefined
    const weight=(v: unknown): v is string => quantity(v) && (!v.includes('.') || !v.endsWith('0'))
    if(weighing!==undefined && (!record(weighing) || !hasExactKeys(weighing,['weighingId','direction','firstWeighingKg','secondWeighingKg','netKg','recordedBy','recordedAtUtc'])
      || !dispatchGuid(weighing.weighingId) || weighing.direction!=='TareFirst' && weighing.direction!=='GrossFirst'
      || !weight(weighing.firstWeighingKg) || !weight(weighing.secondWeighingKg) || !weight(weighing.netKg)
      || !text(weighing.recordedBy) || weighing.recordedBy!==weighing.recordedBy.trim() || weighing.recordedBy.length>128 || !utc(weighing.recordedAtUtc)))return null
    if(!record(facts) || !hasExactKeys(facts,['loadingInstructionId','loadingInstructionLineId','sourceDeliveryId','sourceShipmentId','sourceOrderId','sourceOrderLineId','unitId','quantityUnit','quantity','sourceDeliveredQuantity','reason','requestedBy','occurredAtUtc','requestedAtUtc','status'],['weighing'])
      || !dispatchGuid(value.companyId) || !dispatchGuid(value.itemId)
      || !dispatchGuid(facts.loadingInstructionId) || ![facts.loadingInstructionLineId,facts.sourceDeliveryId,facts.sourceShipmentId,facts.sourceOrderId,facts.sourceOrderLineId,facts.unitId].every(dispatchGuid)
      || facts.quantityUnit!=='Kilogram' && facts.quantityUnit!=='Piece' || !quantity(facts.quantity) || !quantity(facts.sourceDeliveredQuantity)
      || quantityOrder(facts.quantity)>quantityOrder(facts.sourceDeliveredQuantity)
      || facts.quantityUnit==='Piece' && [facts.quantity,facts.sourceDeliveredQuantity].some(q=>q.includes('.') && /[1-9]/.test(q.split('.')[1]!))
      || !text(facts.reason) || facts.reason!==facts.reason.trim() || !text(facts.requestedBy) || facts.requestedBy!==facts.requestedBy.trim() || facts.requestedBy.length>128
      || !utc(facts.occurredAtUtc) || !utc(facts.requestedAtUtc) || /^0001-01-01T00:00:00(?:\.0{1,7})?(?:Z|\+00:00)$/.test(facts.requestedAtUtc)
      || typeof facts.status!=='string' || !['Pending','Approved','Rejected'].includes(facts.status) || value.pending!==(facts.status==='Pending')
      || weighing!==undefined && facts.quantityUnit!=='Piece'
      || value.sourceVersion!==(value.pending?weighing===undefined?'1':'2':weighing===undefined?'2':'3')
      || value.approvalSupported!==(facts.quantityUnit==='Kilogram' || weighing!==undefined)
      || value.references.length!==1 || !record(value.references[0]) || value.references[0].role!=='loading-instruction' || !same(value.references[0].recordId,facts.loadingInstructionId)
      || value.rules.length!==1 || !record(value.rules[0]) || value.rules[0].ruleCode!=='logistics.customer-return-request'
      || value.rules[0].unit!==(facts.quantityUnit==='Kilogram'?'kg':'adet'))return null
  }
  if (kind === 'logistics.dispatch-note.v1') {
    const note = value.logisticsDispatchNote
    // Native decimal(18,3) display preserves trailing zeroes. Never convert quantities or derive kilograms.
    const quantity = (field: unknown): field is string => typeof field === 'string'
      && field === field.trim() && /^(?:0|[1-9]\d{0,14})(?:\.\d{1,3})?$/.test(field) && /[1-9]/.test(field)
    const dateOnly = (field: unknown) => typeof field === 'string' && field.length === 10 && /^\d{4}-\d{2}-\d{2}$/.test(field)
      && field >= '0001-01-01' && field <= '9999-12-31' && Number.isFinite(Date.parse(field + 'T00:00:00Z')) && new Date(field + 'T00:00:00Z').toISOString().slice(0,10) === field
    if (!record(note) || !hasExactKeys(note, ['instructionId','dispatchNoteId','customerPartyId','companyAccountId','effectiveOn','status','number','eDispatchNumber','createdBy','createdAtUtc','lines'])
      || !dispatchGuid(value.companyId) || !dispatchGuid(value.itemId) || !dispatchVersion(value.sourceVersion)
      || !dispatchGuid(note.instructionId) || !dispatchGuid(note.dispatchNoteId) || !same(note.dispatchNoteId,id) || !dispatchGuid(note.customerPartyId) || !dispatchGuid(note.companyAccountId)
      || !dateOnly(note.effectiveOn) || typeof note.status !== 'string' || !['Draft','Approved','Cancelled'].includes(note.status) || value.pending && (note.status !== 'Draft' || value.sourceVersion === '9223372036854775807')
      || !text(note.createdBy) || note.createdBy.length > 128 || !transferDispatchUtc(note.createdAtUtc) || note.createdAtUtc !== note.createdAtUtc.trim()
      || note.number !== null && (!text(note.number) || note.number.length > 28)
      || note.eDispatchNumber !== null && (typeof note.eDispatchNumber !== 'string' || note.eDispatchNumber.length !== 16 || !/^[A-Z0-9]{3}[0-9]{13}$/.test(note.eDispatchNumber) || note.number === null)
      || note.status === 'Draft' && (note.number !== null || note.eDispatchNumber !== null) || note.status === 'Approved' && note.number === null
      || !Array.isArray(note.lines) || note.lines.length === 0 || !note.lines.every((line,index) => record(line)
        && hasExactKeys(line,['lineNo','shipmentId','instructionLineId','sourceOrderId','sourceOrderLineId','productId','unitId','quantity'],['quantityKg'])
        && line.lineNo === index + 1 && [line.shipmentId,line.instructionLineId,line.sourceOrderId,line.sourceOrderLineId,line.productId,line.unitId].every(dispatchGuid)
        && quantity(line.quantity) && (line.quantityKg === undefined || quantity(line.quantityKg)))
      || new Set(note.lines.map(line => ((line as Record<string,unknown>).shipmentId as string).toLowerCase())).size !== note.lines.length
      || value.references.length !== 1 || !record(value.references[0]) || value.references[0].role !== 'loading-instruction' || !same(value.references[0].recordId,note.instructionId)) return null
  }
  if (kind === 'finance.bank-account-iban-change.v1') {
    const facts = value.bankAccountIbanChange
    if (!value.pending || !dispatchVersion(value.sourceVersion) || value.sourceVersion === '9223372036854775807'
      || !record(facts) || !hasExactKeys(facts, ['accountId','companyId','companyLegalName','currentIban','proposedIban','reason','proposedBy','proposedAtUtc'])
      || !dispatchGuid(facts.accountId) || !same(facts.accountId,id) || !dispatchGuid(facts.companyId) || !same(facts.companyId,company)
      || !text(facts.companyLegalName) || facts.companyLegalName.length > 250
      || facts.currentIban !== null && (typeof facts.currentIban !== 'string' || facts.currentIban.length > 34)
      || !text(facts.proposedIban) || facts.proposedIban !== facts.proposedIban.trim() || facts.proposedIban.length < 15 || facts.proposedIban.length > 34
      || !/^[A-Z]{2}[0-9]{2}[A-Z0-9]+$/.test(facts.proposedIban)
      || !text(facts.reason) || facts.reason !== facts.reason.trim() || facts.reason.length > 500
      || !text(facts.proposedBy) || facts.proposedBy.length > 128 || !transferDispatchUtc(facts.proposedAtUtc) || facts.proposedAtUtc !== facts.proposedAtUtc.trim()
      || value.references.filter(ref => record(ref) && ref.role === 'bank-account' && same(ref.recordId,id)).length !== 1
      || value.rules.length !== 1 || !record(value.rules[0]) || value.rules[0].ruleCode !== 'finance.bank-account-iban-change-approval-required'
      || [value.rules[0].threshold,value.rules[0].actual,value.rules[0].deviation,value.rules[0].unit].some(field => field !== null)) return null
  }
  if (kind === 'finance.bank-account.v1') {
    const proof = value.bankAccount
    if (!record(proof) || !hasExactKeys(proof, ['account', 'proposedBy', 'proposedAtUtc']) || !text(proof.proposedBy) || !transferDispatchUtc(proof.proposedAtUtc)) return null
    // This is the native safe seven-field BankAccountView, not a bank card or its disclosure DTO.
    const page = decodeBankAccountPage({companyId:company,items:[proof.account]}, company)
    const account = page.ok ? page.value.items[0] : undefined
    if (account === undefined || !same(account.id, id) || account.displayName.length > 200
      || !isPositiveInt64Text(account.version) || account.version === '9223372036854775807' || account.version !== value.sourceVersion
      || account.isActive !== !value.pending || account.isUsableForReceipt && !account.isActive
      || value.references.filter(ref => record(ref) && ref.role === 'bank-account' && same(ref.recordId, id)).length !== 1
      || value.references.filter(ref => record(ref) && ref.role === 'currency' && same(ref.recordId, account.currencyId)).length !== 1
      || value.rules.length !== 1 || !record(value.rules[0]) || value.rules[0].ruleCode !== 'finance.bank-account-approval-required'
      || [value.rules[0].threshold,value.rules[0].actual,value.rules[0].deviation,value.rules[0].unit].some(field => field !== null)) return null
  }
  if (kind === 'finance.expense-payment.v1' && !validWorkExpensePaymentPreview(value,company,id)) return null
  if (isWorkReversalKind(kind) && !validWorkReversalPreview(value, company, kind, id)) return null
  if (kind === 'finance.official-sales-invoice.v1') {
    const references: readonly unknown[] = value.references
    const raw = value.officialSalesInvoice
    if (!record(raw) || !dispatchGuid(raw.companyId)) return null
    const invoice = decodeOfficialInvoice(raw, raw.companyId, isWorkPackageEffectiveDate)
    if (invoice === null || !same(invoice.companyId, company) || !same(invoice.invoiceId, id)
      || ![invoice.invoiceId,invoice.customerPartyId,invoice.currencyId,invoice.pdfDocumentId,invoice.ettn].every(dispatchGuid)
      || invoice.ettn !== invoice.ettn.toLowerCase() || !canonicalOwnerText(invoice.officialNo,64) || invoice.officialNo !== value.documentLabel
      || OFFICIAL_AMOUNT_FIELDS.some(field => !exactNativeClrAmount(invoice[field], field === 'payableAmount' || field === 'payableFunctionalAmount'))
      || invoice.pdfSha256.length !== 64 || !/^[0-9a-f]+$/.test(invoice.pdfSha256)
      || invoice.version !== (invoice.status === 'PendingApproval' ? '1' : '2') || invoice.version !== value.sourceVersion
      || value.pending !== (invoice.status === 'PendingApproval') || !canonicalOwnerText(invoice.recordedBy,128) || !officialSalesUtc(invoice.recordedAtUtc)
      || [invoice.approvedBy,invoice.rejectedBy,invoice.withdrawnBy].some(actor => actor !== null && !canonicalOwnerText(actor,128))
      || [invoice.approvedAtUtc,invoice.rejectedAtUtc,invoice.withdrawnAtUtc].some(at => at !== null && !officialSalesUtc(at))
      || invoice.rejectionReason !== null && (!canonicalOwnerText(invoice.rejectionReason,1000))
      || invoice.invoiceDate != null && !isWorkPackageEffectiveDate(invoice.invoiceDate)
      || !validOfficialSalesDynamicFacts(invoice) || references.length !== 3
      || [['customer',invoice.customerPartyId],['currency',invoice.currencyId],['pdf-document',invoice.pdfDocumentId]].some(([role,target]) => references.filter(ref=>record(ref)&&ref.role===role&&same(ref.recordId,target!)).length!==1)
      || value.rules.length !== 1 || !record(value.rules[0]) || value.rules[0].ruleCode !== 'finance.official-sales-invoice-approval-required'
      || value.rules[0].threshold !== null || value.rules[0].deviation !== null || value.rules[0].unit !== null
      || !exactNativeClrAmount(value.rules[0].actual,true) || value.rules[0].actual !== exactAmountIdentity(invoice.payableAmount)) return null
  }
  if (kind === 'finance.official-invoice-import.v1') {
    const decoded = decodeDecisionDetail(value.officialInvoiceImport)
    if (!decoded.ok || !same(decoded.value.batch.companyId, company) || !same(decoded.value.batch.batchId, id)
      || value.sourceVersion !== decoded.value.batch.version || value.pending !== (decoded.value.batch.status === 'Pending')
      || value.approvalReasonRequired !== false || value.rejectionSupported !== false || value.references.length !== 0
      || value.rules.length !== 1 || !record(value.rules[0]) || value.rules[0].ruleCode !== 'finance.official-invoice-import-approval'
      || [value.rules[0].threshold,value.rules[0].actual,value.rules[0].deviation,value.rules[0].unit].some(field => field !== null)) return null
  }
  if (kind === 'finance.party-credit-limit.v1') {
    const facts = value.partyCreditLimit
    if (!dispatchGuid(value.companyId) || !dispatchGuid(value.itemId) || !dispatchVersion(value.sourceVersion)
      || value.pending && value.sourceVersion === '9223372036854775807'
      || !record(facts) || !hasExactKeys(facts, ['partyId','companyAccountId','kind','currencyId','amount','baseRevision','currentRevision','proposedBy','proposedAtUtc'])
      || !dispatchGuid(facts.partyId) || !dispatchGuid(facts.companyAccountId) || facts.kind !== 'Set' && facts.kind !== 'Remove'
      || (facts.kind === 'Set' ? !dispatchGuid(facts.currencyId) || !creditLimitAmount(facts.amount) : facts.currencyId !== null || facts.amount !== null)
      || !creditLimitRevision(facts.baseRevision) || !creditLimitRevision(facts.currentRevision)
      || !text(facts.proposedBy) || facts.proposedBy.length > 128 || !transferDispatchUtc(facts.proposedAtUtc) || facts.proposedAtUtc !== facts.proposedAtUtc.trim()
      || value.references.filter(ref => record(ref) && ref.role === 'party' && same(ref.recordId, facts.partyId as string)).length !== 1
      || value.references.filter(ref => record(ref) && ref.role === 'company-account' && same(ref.recordId, facts.companyAccountId as string)).length !== 1
      || facts.kind === 'Set' && value.references.filter(ref => record(ref) && ref.role === 'currency' && same(ref.recordId, facts.currencyId as string)).length !== 1
      || facts.kind === 'Remove' && value.references.some(ref => record(ref) && ref.role === 'currency')) return null
  }
  if (kind === 'inventory.stock-count.v1' || kind === 'inventory.stock-scrap.v1') {
    const scrap = kind === 'inventory.stock-scrap.v1'
    const evidence = scrap ? value.inventoryStockScrap : value.inventoryStockCount
    const quantityKey = scrap ? 'scrapKg' : 'countedKg', resultKey = scrap ? 'remainingKg' : 'differenceKg'
    const occurredKey = scrap ? 'scrappedAtUtc' : 'countedAtUtc', piecesKey = scrap ? 'scrapPieceCount' : 'countedPieceCount'
    const refs = value.references
    // Immutable native quantities; validate their representation, never recompute the remaining kg or difference.
    const stockKg = (v: unknown, signed = false): v is string => typeof v === 'string' && v !== '-0'
      && isCountedKilogramText(signed && v.startsWith('-') ? v.slice(1) : v)
    if (!record(evidence) || !hasExactKeys(evidence, ['labelId', 'labelVersion', 'ownerKind', 'ownerId', 'custodianKind', 'custodianId', 'locationId', 'productId', 'block', 'bookKg', quantityKey, resultKey, occurredKey, 'reason', 'recordedBy', 'recordedAtUtc', 'bookPieceCount', piecesKey])
      || value.sourceVersion !== (value.pending ? '1' : '2') || !isPositiveInt64Text(evidence.labelVersion)
      || !['labelId', 'ownerId', 'custodianId', 'locationId', 'productId'].every(key => isBoundUuid(evidence[key]))
      || (evidence.ownerKind !== 'Company' && evidence.ownerKind !== 'Counterparty') || (evidence.custodianKind !== 'Company' && evidence.custodianKind !== 'Counterparty')
      || (evidence.block !== 'Free' && evidence.block !== 'Blocked') || !stockKg(evidence.bookKg) || !stockKg(evidence[quantityKey]) || !stockKg(evidence[resultKey], !scrap)
      || (scrap ? evidence.bookKg === '0' || evidence.scrapKg === '0' : evidence.differenceKg === '0')
      || !transferDispatchUtc(evidence[occurredKey]) || !transferDispatchUtc(evidence.recordedAtUtc) || !text(evidence.reason) || evidence.reason.length > 1000
      || !text(evidence.recordedBy) || evidence.recordedBy.length > 128
      || (evidence.bookPieceCount === null ? evidence[piecesKey] !== null : !isPieceCount(evidence.bookPieceCount) || !isPieceCount(evidence[piecesKey])
        || scrap && (evidence[piecesKey] as number) > evidence.bookPieceCount)
      || value.rules.length !== 1 || !record(value.rules[0]) || value.rules[0].ruleCode !== (scrap ? 'inventory.stock-scrap-request' : 'inventory.stock-count-difference')
      || value.rules[0].threshold !== null || value.rules[0].actual !== evidence[quantityKey] || value.rules[0].deviation !== (scrap ? null : evidence[resultKey]) || value.rules[0].unit !== 'kg'
      || refs.length !== 3 || refs.filter(ref => record(ref) && ref.role === 'inventory-label' && same(ref.recordId, evidence.labelId as string) && ref.link === null).length !== 1
      || refs.filter(ref => record(ref) && ref.role === 'product' && same(ref.recordId, evidence.productId as string)).length !== 1
      || refs.filter(ref => record(ref) && ref.role === 'location' && same(ref.recordId, evidence.locationId as string)).length !== 1) return null
  }
  if (kind === 'purchasing.supplier-return-cost-exception.v1') {
    const proof = value.supplierReturnCostException
    const orders = value.references.filter(ref => record(ref) && ref.role === 'purchase-order')
    if (!isWorkSupplierReturnVersion(value.sourceVersion) || !record(proof)
      || !hasExactKeys(proof, ['purchaseOrderId', 'purchaseOrderLineId', 'goodsAcceptanceId', 'inventoryMovementId', 'sourceBlock', 'quantityKg', 'replacementExpected', 'reason', 'occurredAtUtc', 'requestedBy', 'requestedAtUtc', 'recordedExpectedPurchaseOrderVersion', 'currentPurchaseOrderVersion', 'undoQuantity', 'priceDisclosure'], ['unitCostTryPerKg'])
      || !['purchaseOrderId', 'purchaseOrderLineId', 'goodsAcceptanceId', 'inventoryMovementId'].every(key => isBoundUuid(proof[key]))
      || orders.length !== 1 || !record(orders[0]) || !same(orders[0].recordId, proof.purchaseOrderId as string)
      || (proof.sourceBlock !== 'Free' && proof.sourceBlock !== 'Blocked') || !canonicalPositiveQuantity(proof.quantityKg)
      || typeof proof.replacementExpected !== 'boolean' || proof.replacementExpected && proof.recordedExpectedPurchaseOrderVersion === null || !isWorkSupplierReturnReason(proof.reason)
      || !transferDispatchUtc(proof.occurredAtUtc) || !transferDispatchUtc(proof.requestedAtUtc) || !text(proof.requestedBy)
      || (proof.recordedExpectedPurchaseOrderVersion === null
        ? proof.currentPurchaseOrderVersion !== null || proof.undoQuantity !== null
        : !isWorkSupplierReturnVersion(proof.recordedExpectedPurchaseOrderVersion) || !isWorkSupplierReturnVersion(proof.currentPurchaseOrderVersion) || !canonicalPositiveQuantity(proof.undoQuantity))
      || value.rules.length !== 2
      || value.rules.filter(rule => record(rule) && rule.ruleCode === 'purchasing.supplier-return-quantity' && rule.actual === proof.quantityKg && rule.unit === 'kg' && rule.threshold === null && rule.deviation === null).length !== 1
      || value.rules.filter(rule => record(rule) && rule.ruleCode === 'purchasing.supplier-return-manual-cost' && rule.threshold === null && rule.deviation === null
        && (proof.priceDisclosure === 'Visible' ? rule.actual === proof.unitCostTryPerKg && rule.unit === 'TRY' : rule.actual === null && rule.unit === null)).length !== 1
      || (proof.priceDisclosure === 'Visible' ? !positiveSupplierUnitCost(proof.unitCostTryPerKg)
        : proof.priceDisclosure !== 'Withheld' || Object.hasOwn(proof, 'unitCostTryPerKg'))) return null
  }
  if (kind === 'logistics.weighing-difference.v1') {
    const evaluation = value.logisticsWeighingDifference
    const notes = value.references.filter(ref => record(ref) && ref.role === 'dispatch-note')
    const instructions = value.references.filter(ref => record(ref) && ref.role === 'loading-instruction')
    if (!isBoundUuid(value.companyId) || !isBoundUuid(value.itemId) || !positiveInt32Text(value.sourceVersion) || !record(evaluation)
      || !hasExactKeys(evaluation, ['dispatchNoteId', 'instructionId', 'theoreticalKg', 'scaleKg', 'differenceKg', 'bandLevel', 'warningBandPercent',
        'approvalBandPercent', 'blockBandPercent', 'totalExcessKg', 'approvalRatePercent', 'decision', 'products', 'enteredBy', 'enteredByComplete', 'recordedBy', 'recordedAtUtc'])
      || !isBoundUuid(evaluation.dispatchNoteId) || !isBoundUuid(evaluation.instructionId)
      || notes.length !== 1 || !record(notes[0]) || !same(notes[0].recordId, evaluation.dispatchNoteId)
      || instructions.length !== 1 || !record(instructions[0]) || !same(instructions[0].recordId, evaluation.instructionId)
      || !['theoreticalKg', 'scaleKg', 'totalExcessKg'].every(key => typeof evaluation[key] === 'string' && KG_TEXT.test(evaluation[key]))
      || typeof evaluation.differenceKg !== 'string' || !KG_SIGNED_TEXT.test(evaluation.differenceKg)
      || typeof evaluation.bandLevel !== 'string' || !['NotEvaluated', 'None', 'Warning', 'Approval', 'Block'].includes(evaluation.bandLevel)
      || typeof evaluation.decision !== 'string' || !['NoExcess', 'WithinRate', 'ApprovalRequired'].includes(evaluation.decision) || value.pending && evaluation.decision !== 'ApprovalRequired'
      || !['warningBandPercent', 'approvalBandPercent', 'blockBandPercent', 'approvalRatePercent'].every(key => evaluation[key] === null || typeof evaluation[key] === 'string' && KG_DECIMAL_TEXT.test(evaluation[key]))
      || !Array.isArray(evaluation.products) || evaluation.products.length < 1 || evaluation.products.length > 200 || !evaluation.products.every(product => isDispatchKilogramsProduct(product) && (product.targetLineNo === null || product.targetLineNo <= 2147483647))
      || new Set(evaluation.products.map(product => product.productId.toLowerCase())).size !== evaluation.products.length
      || evaluation.enteredByComplete !== true || !Array.isArray(evaluation.enteredBy) || evaluation.enteredBy.length < 1 || evaluation.enteredBy.length > 17
      || !evaluation.enteredBy.every(actor => text(actor) && actor === actor.trim() && actor.length <= 128)
      || new Set(evaluation.enteredBy).size !== evaluation.enteredBy.length || !text(evaluation.recordedBy)
      || !evaluation.enteredBy.includes(evaluation.recordedBy) || !transferDispatchUtc(evaluation.recordedAtUtc)) return null
  }
  if (kind === 'inventory.transfer-overage.v1') {
    const proposal = value.inventoryTransferOverage
    const transfers = value.references.filter(ref => record(ref) && ref.role === 'inventory-transfer')
    if (!isBoundUuid(value.companyId) || !isBoundUuid(value.itemId) || !record(proposal)
      || !hasExactKeys(proposal, ['requestNumber', 'requestedAtUtc', 'transferId', 'lineId', 'kilograms', 'pieceCount', 'reason', 'requestedBy', 'receivedBy'])
      || !requestNumber(proposal.requestNumber) || !transferDispatchUtc(proposal.requestedAtUtc)
      || !isBoundUuid(proposal.transferId) || !isBoundUuid(proposal.lineId) || transfers.length !== 1
      || !record(transfers[0]) || !same(transfers[0].recordId, proposal.transferId) || !canonicalPositiveQuantity(proposal.kilograms)
      || !(proposal.pieceCount === null || typeof proposal.pieceCount === 'number' && Number.isInteger(proposal.pieceCount) && proposal.pieceCount >= 0 && proposal.pieceCount <= 2147483647)
      || !text(proposal.reason) || proposal.reason !== proposal.reason.trim() || proposal.reason.length > 500 || nativeControls(proposal.reason)
      || !text(proposal.requestedBy) || proposal.requestedBy.length > 128 || !text(proposal.receivedBy) || proposal.receivedBy.length > 128) return null
  }
  if (kind === 'logistics.plan-excess.v1') {
    const proposal = value.logisticsPlanExcess
    const instructions = value.references.filter(ref => record(ref) && ref.role === 'loading-instruction')
    // Native decimal(18,3), rendered by the owner as 0.###. Validate text without computing a quantity.
    if (!isBoundUuid(value.companyId) || !isBoundUuid(value.itemId) || !record(proposal) || !hasExactKeys(proposal, ['instructionVersion', 'instructionId', 'lineId', 'quantity', 'reason', 'proposedBy', 'proposedAtUtc'])
      || !isPositiveInt64Text(proposal.instructionVersion) || !isBoundUuid(proposal.instructionId) || !isBoundUuid(proposal.lineId)
      || instructions.length !== 1 || !record(instructions[0]) || !same(instructions[0].recordId, proposal.instructionId)
      || !canonicalPositiveQuantity(proposal.quantity)
      || !text(proposal.reason) || proposal.reason.trim() !== proposal.reason || proposal.reason.length > 500 || nativeControls(proposal.reason)
      || !text(proposal.proposedBy) || proposal.proposedBy.length > 128 || !transferDispatchUtc(proposal.proposedAtUtc)) return null
  }
  if (isCounterpartyWorkDecisionKind(kind)) {
    const tax = kind === 'counterparty.tax-identity-change.v1'
    const proof = tax ? value.counterpartyTaxIdentityChange : value.counterpartyLegalNameChange
    const parties = value.references.filter(ref => record(ref) && ref.role === 'party')
    if (!record(proof) || !hasExactKeys(proof, tax
      ? ['partyId', 'changeRequestId', 'currentTaxIdentityNumber', 'proposedTaxIdentityNumber', 'proposedKind', 'reason']
      : ['partyId', 'changeRequestId', 'currentLegalName', 'proposedLegalName', 'reason'])
      || !isBoundUuid(proof.partyId) || !isBoundUuid(proof.changeRequestId) || !same(proof.changeRequestId, id) || parties.length !== 1
      || !record(parties[0]) || !same(parties[0].recordId, proof.partyId)
      || !text(proof.reason) || proof.reason.length > LEGAL_NAME_REASON_MAX) return null
    if (tax) {
      // The correction's historical current value follows the native SQL stored-format constraint,
      // not a new checksum gate. Only the proposed replacement is a validated native number.
      if (typeof proof.currentTaxIdentityNumber !== 'string' || !/^[0-9]{10,11}$/.test(proof.currentTaxIdentityNumber)
        || typeof proof.proposedTaxIdentityNumber !== 'string' || proof.proposedTaxIdentityNumber.trim() !== proof.proposedTaxIdentityNumber || !checkTaxIdentity(proof.proposedTaxIdentityNumber).ok
        || proof.proposedKind !== (proof.proposedTaxIdentityNumber.length === 10 ? 'vkn' : 'tckn')) return null
    } else if (!text(proof.currentLegalName) || proof.currentLegalName.length > LEGAL_NAME_MAX
      || !text(proof.proposedLegalName) || proof.proposedLegalName.length > LEGAL_NAME_MAX) return null
  }
  if (kind === 'inventory.transfer-loss.v1') {
    const loss = value.inventoryTransferLoss
    if (!record(loss) || !hasExactKeys(loss, ['requestNumber', 'requestedAtUtc']) || !requestNumber(loss.requestNumber)
      || !transferDispatchUtc(loss.requestedAtUtc)) return null
  }
  if (kind === 'manufacturing.plan.v1') {
    const options = value.manufacturingPlanReapproval
    if (options !== undefined && options !== null && (!record(options) || !hasExactKeys(options, ['parentRollIds'])
      || !labelIds(options.parentRollIds) || !value.pending || value.rejectionSupported !== false)) return null
    if (value.pending && value.rejectionSupported === false && options == null) return null
    if ((options != null) !== (value.pending && value.rules.some(rule => record(rule) && rule.ruleCode === 'manufacturing.plan-reapproval-after-order-cancel'))) return null
  }
  return value as unknown as WorkDecisionPreview
}
function validWorkExpensePaymentPreview(value: Record<string,unknown>, company: string, id: string): boolean {
  const human=value.expensePayment
  if (!record(human) || !hasExactKeys(human,['payment','treasuryAccountLabel','revisers','draftFactsDigest']) || !record(human.payment)
    || !text(human.treasuryAccountLabel) || typeof human.draftFactsDigest !== 'string' || human.draftFactsDigest.length !== 64 || !/^[0-9a-f]+$/.test(human.draftFactsDigest)
    || !Array.isArray(human.revisers) || !human.revisers.every(actor=>canonicalOwnerText(actor,128)) || new Set(human.revisers).size !== human.revisers.length) return false
  const payment=human.payment
  if (!dispatchGuid(payment.companyId) || !same(payment.companyId,company) || !dispatchGuid(payment.id) || !same(payment.id,id)
    || !dispatchVersion(payment.version) || payment.version !== value.sourceVersion || value.pending !== (payment.status === 'Draft')
    || value.pending && payment.version === '9223372036854775807' || !officialSalesUtc(payment.preparedAtUtc)
    || payment.approvedAtUtc !== null && !officialSalesUtc(payment.approvedAtUtc) || payment.recordedAtUtc !== null && !officialSalesUtc(payment.recordedAtUtc)
    || !canonicalOwnerText(payment.preparedBy,128) || payment.approvedBy !== null && !canonicalOwnerText(payment.approvedBy,128)
    || payment.recordedBy !== null && !canonicalOwnerText(payment.recordedBy,128) || !record(payment.preparation)) return false
  const p=payment.preparation
  if (!reversalGuidFields(p,['treasuryAccountId','expenseItemId']) || !record(p.money) || !dispatchGuid(p.money.currencyId) || !reversalAmount(p.money.amount)
    || !record(p.profile) || !dispatchGuid(p.profile.profileId) || !dispatchVersion(p.profile.version)) return false
  const decoded=decodeExpensePaymentView(payment,payment.companyId,isWorkPackageEffectiveDate)
  if (!decoded.ok || value.approvalReasonRequired !== false || value.rejectionSupported !== false) return false
  const refs=value.references,rules=value.rules
  if (!Array.isArray(refs) || refs.length!==3 || !Array.isArray(rules) || rules.length!==1 || !record(rules[0])
    || rules[0].ruleCode!=='finance.expense-payment-approval-required' || rules[0].threshold!==null || rules[0].deviation!==null || rules[0].unit!==null || !reversalAmount(rules[0].actual,false)) return false
  const targets=[[p.sourceKind==='Bank'?'bank-account':'cash-account',p.treasuryAccountId],['expense-item',p.expenseItemId],['currency',p.money.currencyId]] as const
  return targets.every(([role,target])=>refs.filter(ref=>record(ref) && ref.role===role && same(ref.recordId,target as string) && ref.link===null).length===1)
}
// Both owner views retain arbitrary-precision money text. No Number/CLR-range conversion or POS arithmetic.
const reversalAmount = (value: unknown, positive = true): value is string => typeof value === 'string' && value === value.trim()
  && !nativeControls(value) && (positive ? isPositiveDecimalText(value) : isNonNegativeDecimalText(value))
const reversalDate = (value: unknown) => value === null || isWorkPackageEffectiveDate(value)
const reversalGuidFields = (value: Record<string, unknown>, fields: readonly string[]) => fields.every(field => dispatchGuid(value[field]))
function validWorkReversalPreview(value: Record<string, unknown>, company: string, kind: WorkReversalKind, id: string): boolean {
  const payment = kind === 'finance.manual-bank-payment-reversal.v1'
  const human = payment ? value.manualBankPaymentReversal : value.manualReceiptReversal
  if (!record(human) || !hasExactKeys(human, payment
    ? ['companyId','proposal','paymentDocumentNumber','partyId','companyAccountId','treasuryAccountId','currencyId','amount','paymentOccurredOn','paymentValueOn','originalEventId','paymentRecordedBy','paymentRecordedAtUtc','recordedFactsDigest','treasuryAccountLabel']
    : ['companyId','proposal','originalReceipt','treasuryAccountLabel']) || !dispatchGuid(human.companyId) || !same(human.companyId, company)
    || !text(human.treasuryAccountLabel) || !record(human.proposal)) return false
  const rawProposal = human.proposal
  if (!reversalGuidFields(rawProposal, ['id', payment ? 'paymentId' : 'originalSourceId']) || !same(rawProposal.id,id)
    || !canonicalOwnerText(rawProposal.reason,1000) || !canonicalOwnerText(rawProposal.proposedBy,128) || !officialSalesUtc(rawProposal.proposedAtUtc)
    || rawProposal.decidedBy !== null && !canonicalOwnerText(rawProposal.decidedBy,128)
    || rawProposal.decidedAtUtc !== null && !officialSalesUtc(rawProposal.decidedAtUtc)
    || rawProposal.decisionReason !== null && !canonicalOwnerText(rawProposal.decisionReason,1000)
    || rawProposal.decidedBy !== null && rawProposal.decidedBy === rawProposal.proposedBy
    || rawProposal[payment ? 'reversalEventId' : 'reversalSourceId'] !== null && !dispatchGuid(rawProposal[payment ? 'reversalEventId' : 'reversalSourceId'])) return false
  const decoded = payment ? decodePaymentReversalProposal(rawProposal, isWorkPackageEffectiveDate) : decodeReversalProposal(rawProposal, isWorkPackageEffectiveDate)
  if (!decoded.ok || decoded.value.version !== (value.pending ? '1' : '2') || value.sourceVersion !== decoded.value.version
    || value.pending !== (decoded.value.status === 'Pending') || value.approvalReasonRequired !== false || value.rejectionSupported !== true) return false
  const originalId = rawProposal[payment ? 'paymentId' : 'originalSourceId'] as string
  const original = payment ? human : human.originalReceipt
  if (!record(original) || !reversalGuidFields(original, ['partyId','companyAccountId','treasuryAccountId','currencyId','originalEventId'])) return false
  if (payment) {
    if (human.paymentDocumentNumber !== null && typeof human.paymentDocumentNumber !== 'string' || !reversalAmount(human.amount)
      || !isWorkPackageEffectiveDate(human.paymentOccurredOn) || !reversalDate(human.paymentValueOn)
      || !canonicalOwnerText(human.paymentRecordedBy,128) || !officialSalesUtc(human.paymentRecordedAtUtc)
      || typeof human.recordedFactsDigest !== 'string' || human.recordedFactsDigest.length !== 64 || !/^[0-9a-f]+$/.test(human.recordedFactsDigest)) return false
  } else {
    if (!hasExactKeys(original, ['sourceId','sourceVersion','documentNumber','partyId','companyAccountId','treasuryAccountId','currencyId','amountText','occurredOn','valueOn','posCommissionAmountText','posBankNetAmountText','posCommissionBearer','originalEventId','recordedBy','recordedAtUtc'])
      || !dispatchGuid(original.sourceId) || !same(original.sourceId, originalId) || !dispatchVersion(original.sourceVersion)
      || original.documentNumber !== null && typeof original.documentNumber !== 'string' || !reversalAmount(original.amountText)
      || !isWorkPackageEffectiveDate(original.occurredOn) || !reversalDate(original.valueOn)
      || !canonicalOwnerText(original.recordedBy,128) || !officialSalesUtc(original.recordedAtUtc)
      || !(original.posCommissionAmountText === null && original.posBankNetAmountText === null && original.posCommissionBearer === null)
        && (!(original.posCommissionBearer === 'Company' || original.posCommissionBearer === 'Customer')
          || !reversalAmount(original.posCommissionAmountText,false) || !reversalAmount(original.posBankNetAmountText))) return false
    if (rawProposal.reversalSourceId !== null && same(rawProposal.reversalSourceId,originalId)) return false
  }
  const refs = value.references, rules = value.rules
  if (!Array.isArray(refs) || refs.length !== 4 || !Array.isArray(rules) || rules.length !== 1 || !record(rules[0])
    || rules[0].ruleCode !== (payment ? 'finance.manual-bank-payment-reversal-approval-required' : 'finance.manual-receipt-reversal-approval-required')
    || rules[0].threshold !== null || rules[0].deviation !== null || rules[0].unit !== null || !reversalAmount(rules[0].actual,false)) return false
  // The native server MatchesDecisionItem formatter owns the existing rule amount. It may differ in representation
  // from full native money text; do not replace it with a new client decimal/rounding policy.
  const nativePath = payment ? '/finans/odemeler/' : '/finans/tahsilatlar/'
  const targets = [[payment ? 'manual-bank-payment' : 'manual-receipt',originalId],['party',original.partyId],['bank-account',original.treasuryAccountId],['currency',original.currencyId]] as const
  return targets.every(([role,target]) => refs.filter(ref => record(ref) && ref.role === role && same(ref.recordId, target as string)
    && (role === 'manual-bank-payment' || role === 'manual-receipt' ? same(ref.link, nativePath + originalId) : ref.link === null)).length === 1)
}
const canonicalOwnerText = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim().length > 0 && value === value.trim() && value.length <= max
const officialSalesUtc = (value: unknown): value is string => transferDispatchUtc(value) && value === value.trim()
  && /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
// Exact digit identity only; no amount calculation, conversion or rewriting of the displayed native text.
const exactAmountIdentity = (value: string): string => {
  const [whole,fraction=''] = value.split('.')
  const tail = fraction.replace(/0+$/, '')
  return (whole!.replace(/^0+/, '') || '0') + (tail === '' ? '' : '.' + tail)
}
const nativeAmountText = (value: string) => value === value.trim() && isNonNegativeDecimalText(value)
const validOfficialSalesDynamicFacts = (invoice: OfficialInvoice): boolean => {
  if (invoice.sourceDifferences?.some(diff => !dispatchGuid(diff.importBatchId) || !dispatchGuid(diff.sourceDocumentId)
    || !canonicalOwnerText(diff.officialNo,64) || !dispatchGuid(diff.ettn)
    || diff.invoiceDate !== null && !isWorkPackageEffectiveDate(diff.invoiceDate)
    || diff.payableFunctionalAmount !== null && !nativeAmountText(diff.payableFunctionalAmount))) return false
  const warning = invoice.taxWarning
  return warning == null || warning.code === 'finance_official_invoice_tax_inconsistent'
    && (warning.sourceDocumentId === null || dispatchGuid(warning.sourceDocumentId))
    && nativeAmountText(warning.expectedPayableFunctionalAmount.startsWith('-') ? warning.expectedPayableFunctionalAmount.slice(1) : warning.expectedPayableFunctionalAmount)
    && nativeAmountText(warning.payableFunctionalAmount) && nativeAmountText(warning.differenceFunctionalAmount)
    && isPositiveDecimalText(warning.differenceFunctionalAmount)
}
export const fetchWorkDecision = (company: string, kind: WorkDecisionKind, id: string, signal?: AbortSignal) =>
  readJson(path(kind, id), company, value => decodeWorkDecisionPreview(value, company, kind, id), signal)
export async function sendWorkDecision(company: string, kind: WorkDecisionKind, id: string, request: WorkDecisionRequest, key: string): Promise<CommandOutcome<WorkDecisionResult>> {
  if (!isWorkDecisionRequest(request, kind)) return { type: 'unknown', reason: 'unexpected' }
  let response: Response
  try { response = await fetch(API_BASE + path(kind, id), { method: 'POST', headers: { ...devHeaders(),
    'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(request) }) }
  catch { return { type: 'unknown', reason: 'network-error' } }
  const body: unknown = await response.json().catch(() => null)
  if (response.status === 200) {
    const state = kind === 'product.price-list-version.v1' ? 'Published' : kind === 'purchasing.supplier-return-cost-exception.v1' ? request.approve ? 'Recorded' : 'Rejected' : kind === 'manufacturing.plan.v1' ? request.approve ? 'Approved' : 'Draft' : kind === 'purchasing.goods-receipt-batch.v1' ? request.approve ? 'Posted' : 'Rejected' : kind === 'purchasing.incoming-invoice-variance.v1' ? 'Matched' : kind === 'finance.cash-account-passivation.v1' ? request.approve ? 'Passive' : 'Approved'
      : kind === 'finance.cash-account-reactivation.v1' ? request.approve ? 'Approved' : 'Passive' : request.approve ? 'Approved' : 'Rejected'
    // Purchasing replays its current request. A successful approval can later be rejected natively;
    // withdrawal/draft/submission cannot follow a recorded approval, and rejection is terminal.
    const currentPurchaseRejection = kind === 'purchasing.request.v1' && request.approve
      && record(body) && body.replayed === true && body.state === 'Rejected'
    // Ordinary invoice receipts read the current invoice. Matching keeps Approved; only
    // finalization and proved official cancellation can follow its recorded approval.
    // Subcontract receipts reconstruct their immutable lifecycle outcome instead.
    const currentInvoiceApproval = kind === 'sales.invoice.v1' && request.approve
      && record(body) && body.replayed === true && (body.state === 'Finalized' || body.state === 'OfficiallyCancelled')
    // Orders replay their current approval axis. An approved revision can be revised,
    // submitted and rejected again; only a never-approved Rev.0 can be withdrawn.
    const currentOrderDecision = kind === 'sales.order-draft.v1' && record(body) && body.replayed === true
      && (body.state === 'Preparing' || body.state === 'PendingApproval'
        || (request.approve ? body.state === 'Rejected' : body.state === 'Approved' || body.state === 'Withdrawn'))
    // Native purchase order approval receipts read the current order; a supplier revision can reopen
    // the draft, and lawful cancellation can close its remaining shares. Neither is a rejection.
    const currentPurchaseOrder = kind === 'purchasing.order.v1' && request.approve && record(body) && body.replayed === true
      && (body.state === 'Draft' || body.state === 'Cancelled')
    return (kind !== 'finance.expense-payment.v1' && kind !== 'purchasing.order.v1' && kind !== 'purchasing.service-order.v1' && kind !== 'purchasing.incoming-invoice-variance.v1' || request.approve) && record(body) && hasExactKeys(body, ['companyId', 'kind', 'itemId', 'state', 'replayed', 'originalCorrelationId'])
      && same(body.companyId, company) && body.kind === kind && same(body.itemId, id) && (body.state === state || currentPurchaseRejection || currentInvoiceApproval || currentOrderDecision || currentPurchaseOrder)
      && typeof body.replayed === 'boolean' && text(body.originalCorrelationId) && replayHeadersAgree(response.headers, body.replayed, body.originalCorrelationId)
      ? { type: 'applied', data: body as unknown as WorkDecisionResult, replayed: body.replayed } : { type: 'unknown', reason: 'unexpected' }
  }
  const replayed = response.headers.get('Idempotency-Replayed')
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  if (replayed !== null && replayed !== 'true' || replayed === 'true' && (original === null || !original.trim())
    || replayed === null && original !== null) return { type: 'unknown', reason: 'unexpected' }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) return { type: 'refused', status: response.status, code: null,
    companyId: null, groupCode: null, replayed: response.headers.get('Idempotency-Replayed') === 'true' }
  return { type: 'unknown', reason: 'server' }
}
