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
  'finance.credit-account.v1', 'finance.cash-account-passivation.v1', 'finance.cash-account-reactivation.v1','sales.order-draft.v1','sales.invoice.v1','sales.subcontract-invoice.v1','purchasing.request.v1','purchasing.order.v1','purchasing.service-order.v1','purchasing.incoming-invoice-variance.v1','purchasing.goods-receipt-batch.v1','manufacturing.plan.v1','inventory.transfer-loss.v1','counterparty.tax-identity-change.v1','counterparty.legal-name-change.v1','logistics.plan-excess.v1','inventory.transfer-overage.v1','logistics.weighing-difference.v1','purchasing.supplier-return-cost-exception.v1','inventory.stock-count.v1','finance.bank-account.v1'] as const
// Native owner purposes: kind prefixes do not define their actual capability ids.
export const COUNTERPARTY_WORK_DECISION_CAPABILITY = {
  'counterparty.tax-identity-change.v1': 'cari.tax-identity-change.work-decision',
  'counterparty.legal-name-change.v1': 'cari.legal-name-change.work-decision',
} as const
export type CounterpartyWorkDecisionKind = keyof typeof COUNTERPARTY_WORK_DECISION_CAPABILITY
export type WorkDecisionKind = typeof WORK_DECISION_KINDS[number] | CounterpartyWorkDecisionKind
export const isCounterpartyWorkDecisionKind = (kind: string): kind is CounterpartyWorkDecisionKind =>
  Object.hasOwn(COUNTERPARTY_WORK_DECISION_CAPABILITY, kind)
export const isSalesWorkDecisionKind = (kind: WorkDecisionKind) => kind === 'sales.order-draft.v1' || kind === 'sales.invoice.v1' || kind === 'sales.subcontract-invoice.v1'
/** Native owner reason/note bounds; Counterparty notes and Sales rejection reasons are optional. */
export const workDecisionReasonLimit = (kind: WorkDecisionKind) => kind === 'inventory.stock-count.v1' || isCounterpartyWorkDecisionKind(kind) ? 1000 : kind === 'manufacturing.plan.v1' ? 256 : kind === 'purchasing.incoming-invoice-variance.v1' ? 2000 : kind === 'purchasing.service-order.v1' ? 1000 : isSalesWorkDecisionKind(kind) || kind === 'purchasing.request.v1' ? 4000 : 500
export const isWorkDecisionKind = (value: string): value is WorkDecisionKind => isCounterpartyWorkDecisionKind(value) || WORK_DECISION_KINDS.some(kind => kind === value)
export interface WorkDecisionRequest { readonly expectedVersion: string; readonly approve: boolean; readonly reason: string | null; readonly delegationId: string | null; readonly supplierReturnCostException?: { readonly expectedPurchaseOrderVersion?: string }; readonly inventoryTransferOverage?: { readonly requestNumber: number; readonly decidedAtUtc: string }; readonly logisticsPlanExcess?: { readonly expectedInstructionVersion: string }; readonly inventoryTransferLoss?: { readonly requestNumber: number; readonly decidedAtUtc: string }; readonly manufacturingPlanReapproval?: { readonly selectedLabelIds?: readonly string[] | null } | null }
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
export interface WorkBankAccountProof { readonly account: BankAccountView; readonly proposedBy: string; readonly proposedAtUtc: string }
export interface WorkDecisionPreview {
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
export const isWorkDecisionRequest = (value: unknown, kind?: WorkDecisionKind): value is WorkDecisionRequest => {
  if (!record(value) || !hasExactKeys(value, ['expectedVersion', 'approve', 'reason', 'delegationId'], kind === 'purchasing.supplier-return-cost-exception.v1' ? ['supplierReturnCostException'] : kind === 'inventory.transfer-overage.v1' ? ['inventoryTransferOverage'] : kind === 'logistics.plan-excess.v1' ? ['logisticsPlanExcess'] : kind === 'inventory.transfer-loss.v1' ? ['inventoryTransferLoss'] : kind === 'manufacturing.plan.v1' ? ['manufacturingPlanReapproval'] : [])
    || !text(value.expectedVersion) || (kind === 'inventory.transfer-overage.v1' || kind === 'logistics.plan-excess.v1' || kind === 'manufacturing.plan.v1' || kind === 'inventory.transfer-loss.v1' || kind !== undefined && isCounterpartyWorkDecisionKind(kind)) && !isPositiveInt64Text(value.expectedVersion) || typeof value.approve !== 'boolean' || !nullableText(value.reason)
    || value.delegationId !== null && !isBoundUuid(value.delegationId)) return false
  if (kind === 'finance.bank-account.v1') return value.approve && value.reason === null && isPositiveInt64Text(value.expectedVersion) && value.expectedVersion !== '9223372036854775807'
  if (kind === 'inventory.stock-count.v1') return value.expectedVersion === '1' && isWorkStockCountReason(value.reason)
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
  if (!record(value) || !hasExactKeys(value, ['companyId', 'kind', 'itemId', 'documentLabel', 'sourceVersion', 'pending', 'approvalReasonRequired', 'references', 'rules'], kind === 'finance.bank-account.v1' ? ['rejectionSupported', 'bankAccount'] : kind === 'inventory.stock-count.v1' ? ['rejectionSupported', 'inventoryStockCount'] : kind === 'purchasing.supplier-return-cost-exception.v1' ? ['rejectionSupported', 'supplierReturnCostException'] : kind === 'logistics.weighing-difference.v1' ? ['rejectionSupported', 'logisticsWeighingDifference'] : kind === 'inventory.transfer-overage.v1' ? ['rejectionSupported', 'inventoryTransferOverage'] : kind === 'logistics.plan-excess.v1' ? ['rejectionSupported', 'logisticsPlanExcess'] : kind === 'inventory.transfer-loss.v1' ? ['rejectionSupported', 'inventoryTransferLoss'] : kind === 'manufacturing.plan.v1' ? ['rejectionSupported', 'manufacturingPlanReapproval'] : kind === 'counterparty.tax-identity-change.v1' ? ['rejectionSupported', 'counterpartyTaxIdentityChange'] : kind === 'counterparty.legal-name-change.v1' ? ['rejectionSupported', 'counterpartyLegalNameChange'] : ['rejectionSupported'])
    || !same(value.companyId, company) || value.kind !== kind || !same(value.itemId, id) || !text(value.documentLabel)
    || (kind === 'inventory.opening-cost.v1' ? value.sourceVersion !== `submission/${id}` : !isPositiveInt64Text(value.sourceVersion))
    || typeof value.pending !== 'boolean' || value.approvalReasonRequired !== (kind === 'inventory.stock-count.v1' || kind === 'inventory.transfer-overage.v1' || kind === 'inventory.transfer-loss.v1' || kind === 'inventory.opening-cost.v1' || kind === 'purchasing.service-order.v1' || kind === 'purchasing.incoming-invoice-variance.v1')
    || (kind === 'manufacturing.plan.v1' ? typeof value.rejectionSupported !== 'boolean' : kind === 'inventory.stock-count.v1' || kind === 'purchasing.supplier-return-cost-exception.v1' || kind === 'purchasing.goods-receipt-batch.v1' || kind === 'inventory.transfer-overage.v1' || kind === 'inventory.transfer-loss.v1' || isCounterpartyWorkDecisionKind(kind) ? value.rejectionSupported !== true : kind === 'finance.bank-account.v1' || kind === 'logistics.weighing-difference.v1' || kind === 'logistics.plan-excess.v1' || kind === 'purchasing.order.v1' || kind === 'purchasing.service-order.v1' || kind === 'purchasing.incoming-invoice-variance.v1' ? value.rejectionSupported !== false : value.rejectionSupported !== undefined && value.rejectionSupported !== true)
    || !Array.isArray(value.references) || value.references.length > 12 || !value.references.every(ref => record(ref)
      && hasExactKeys(ref, ['role', 'label', 'recordId', 'link']) && text(ref.role) && text(ref.label)
      && (ref.recordId === null || isBoundUuid(ref.recordId)) && nullableText(ref.link))
    || !Array.isArray(value.rules) || value.rules.length > 8 || !value.rules.every(rule => record(rule)
      && hasExactKeys(rule, ['ruleCode', 'threshold', 'actual', 'deviation', 'unit']) && text(rule.ruleCode)
      && [rule.threshold, rule.actual, rule.deviation, rule.unit].every(nullableText))) return null
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
  if (kind === 'inventory.stock-count.v1') {
    const count = value.inventoryStockCount
    const refs = value.references
    // Native persisted quantities, already evaluated by Inventory. No subtraction or piece conversion here.
    const countKg = (v: unknown, signed = false): v is string => typeof v === 'string' && v !== '-0'
      && isCountedKilogramText(signed && v.startsWith('-') ? v.slice(1) : v)
    if (!record(count) || !hasExactKeys(count, ['labelId', 'labelVersion', 'ownerKind', 'ownerId', 'custodianKind', 'custodianId', 'locationId', 'productId', 'block', 'bookKg', 'countedKg', 'differenceKg', 'countedAtUtc', 'reason', 'recordedBy', 'recordedAtUtc', 'bookPieceCount', 'countedPieceCount'])
      || value.sourceVersion !== (value.pending ? '1' : '2') || !isPositiveInt64Text(count.labelVersion)
      || !['labelId', 'ownerId', 'custodianId', 'locationId', 'productId'].every(key => isBoundUuid(count[key]))
      || (count.ownerKind !== 'Company' && count.ownerKind !== 'Counterparty') || (count.custodianKind !== 'Company' && count.custodianKind !== 'Counterparty')
      || (count.block !== 'Free' && count.block !== 'Blocked') || !countKg(count.bookKg) || !countKg(count.countedKg) || !countKg(count.differenceKg, true) || count.differenceKg === '0'
      || !transferDispatchUtc(count.countedAtUtc) || !transferDispatchUtc(count.recordedAtUtc) || !text(count.reason) || count.reason.length > 1000
      || !text(count.recordedBy) || count.recordedBy.length > 128
      || (count.bookPieceCount === null ? count.countedPieceCount !== null : !isPieceCount(count.bookPieceCount) || !isPieceCount(count.countedPieceCount))
      || value.rules.length !== 1 || !record(value.rules[0]) || value.rules[0].ruleCode !== 'inventory.stock-count-difference'
      || value.rules[0].threshold !== null || value.rules[0].actual !== count.countedKg || value.rules[0].deviation !== count.differenceKg || value.rules[0].unit !== 'kg'
      || refs.length !== 3 || refs.filter(ref => record(ref) && ref.role === 'inventory-label' && same(ref.recordId, count.labelId as string) && ref.link === null).length !== 1
      || refs.filter(ref => record(ref) && ref.role === 'product' && same(ref.recordId, count.productId as string)).length !== 1
      || refs.filter(ref => record(ref) && ref.role === 'location' && same(ref.recordId, count.locationId as string)).length !== 1) return null
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
    const state = kind === 'purchasing.supplier-return-cost-exception.v1' ? request.approve ? 'Recorded' : 'Rejected' : kind === 'manufacturing.plan.v1' ? request.approve ? 'Approved' : 'Draft' : kind === 'purchasing.goods-receipt-batch.v1' ? request.approve ? 'Posted' : 'Rejected' : kind === 'purchasing.incoming-invoice-variance.v1' ? 'Matched' : kind === 'finance.cash-account-passivation.v1' ? request.approve ? 'Passive' : 'Approved'
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
    return (kind !== 'purchasing.order.v1' && kind !== 'purchasing.service-order.v1' && kind !== 'purchasing.incoming-invoice-variance.v1' || request.approve) && record(body) && hasExactKeys(body, ['companyId', 'kind', 'itemId', 'state', 'replayed', 'originalCorrelationId'])
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
