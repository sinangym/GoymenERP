import {decodeSalesContract, SALES_CONTRACT_STATE, type SalesContract} from '../sales/salesContract'
import {isBoundUuid} from '../finance/contract'
import {hasExactKeys} from '../finance/wireShape'
import {salesRequestVersion, salesRequestIncrementable} from './workSalesRequestDecision'

export const SALES_CONTRACT_DISCOUNT_WORK_KIND = 'sales.contract-discount.v1'
export const SALES_CONTRACT_DISCOUNT_WORK_CAPABILITY = 'sales.contract-discount.work-decision'
export interface WorkSalesContractDiscountPreview {
  readonly companyId: string
  readonly contract: SalesContract
  readonly approvalSupported: boolean
  readonly rejectionSupported: boolean
}
export interface SalesContractDiscountWorkCommand {
  readonly expectedVersion: string
  readonly approve: boolean
  readonly reason: string | null
  readonly delegationId: string | null
}
const record = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && v.length === 36 && isBoundUuid(v)
const same = (v: unknown, id: string) => typeof v === 'string' && v.toLowerCase() === id.toLowerCase()
const actor = (v: string) => v.trim() !== '' && v === v.trim() && v.length <= 128
const utc = (v: string, date: (v: unknown) => boolean) => v === v.trim() && date(v.slice(0, 10))
  && /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
export const validSalesContractDiscountReason = (approve: boolean, value: unknown) => approve ? value === null
  : typeof value === 'string' && value === value.trim() && value.length >= 1 && value.length <= 4000

/** Reuse the native full24/3/4 shape and state checks. Work adds exact wire bounds; it never calculates amounts or approval rules. */
export function decodeWorkSalesContractDiscountPreview(value: unknown, company: string, id: string,
  date: (v: unknown) => boolean, nativeAmount: (v: unknown) => boolean): WorkSalesContractDiscountPreview | null {
  if (!guid(company) || !guid(id) || !record(value)
    || !hasExactKeys(value, ['companyId', 'contract', 'approvalSupported', 'rejectionSupported'])
    || !same(value.companyId, company) || !record(value.contract)
    || typeof value.contract.moneyScale !== 'number' || !Number.isInteger(value.contract.moneyScale)
    || value.contract.moneyScale < 0 || value.contract.moneyScale > 28) return null
  const c = decodeSalesContract(value.contract, id, {dateOnly: date, pendingVersion: salesRequestVersion})
  if (c === null || ![c.id, c.partyId, c.companyAccountId, c.currencyId].every(guid)
    || c.contractNo > 2147483647 || !salesRequestVersion(c.version) || !actor(c.createdBy) || !utc(c.createdAtUtc, date)
    || c.decidedBy !== null && !actor(c.decidedBy) || c.decidedAtUtc !== null && !utc(c.decidedAtUtc, date)
    || c.amountDisclosure === 'Visible' && (!nativeAmount(c.plannedTotalAmount) || c.installments.some(x => !nativeAmount(x.amount))
      || c.discountRules.some(x => !nativeAmount(x.ratePercent)) || c.discountLimitPercent !== null && !nativeAmount(c.discountLimitPercent))) return null
  const actionable = c.state === SALES_CONTRACT_STATE.pending && salesRequestIncrementable(c.version)
  if (value.approvalSupported !== actionable || value.rejectionSupported !== actionable) return null
  return {companyId: value.companyId as string, contract: c, approvalSupported: actionable, rejectionSupported: actionable}
}

export const isSalesContractDiscountWorkCommand = (value: unknown): value is SalesContractDiscountWorkCommand => record(value)
  && hasExactKeys(value, ['expectedVersion', 'approve', 'reason', 'delegationId'])
  && salesRequestIncrementable(value.expectedVersion) && typeof value.approve === 'boolean'
  && validSalesContractDiscountReason(value.approve, value.reason) && (value.delegationId === null || guid(value.delegationId))

/** Prepare the canonical body before the caller presents its confirmation; retries keep this exact prepared body. */
export function salesContractDiscountWorkCommand(human: WorkSalesContractDiscountPreview, approve: boolean, reason: string,
  delegationId: string | null): SalesContractDiscountWorkCommand | null {
  if (!(approve ? human.approvalSupported : human.rejectionSupported)) return null
  const body = {expectedVersion: human.contract.version, approve, reason: approve ? null : reason.trim(), delegationId}
  return isSalesContractDiscountWorkCommand(body) ? body : null
}

export function validSalesContractDiscountPreview(value: Record<string, unknown>, company: string, id: string,
  date: (v: unknown) => boolean, nativeAmount: (v: unknown) => boolean): boolean {
  const h = decodeWorkSalesContractDiscountPreview(value.salesContractDiscount, company, id, date, nativeAmount)
  if (h === null || !hasExactKeys(value, ['companyId', 'kind', 'itemId', 'documentLabel', 'sourceVersion', 'pending',
    'approvalReasonRequired', 'references', 'rules', 'approvalSupported', 'rejectionSupported', 'salesContractDiscount'])
    || !same(value.companyId, company) || !same(value.itemId, id) || value.kind !== SALES_CONTRACT_DISCOUNT_WORK_KIND
    || value.documentLabel !== `Sözleşme #${h.contract.contractNo}` || value.sourceVersion !== h.contract.version
    || value.pending !== (h.contract.state === SALES_CONTRACT_STATE.pending) || value.approvalReasonRequired !== false
    || value.approvalSupported !== h.approvalSupported || value.rejectionSupported !== h.rejectionSupported
    || !Array.isArray(value.references) || value.references.length !== 1 || !Array.isArray(value.rules)
    || value.rules.length < 1 || value.rules.length > Math.min(8, h.contract.discountRules.length)
    || !h.contract.discountLimitDefined && value.rules.length !== 1) return false
  const ref = value.references[0]
  if (!record(ref) || !hasExactKeys(ref, ['role', 'label', 'recordId', 'link']) || ref.role !== 'sales-contract'
    || ref.label !== value.documentLabel || !same(ref.recordId, id) || ref.link !== null) return false
  return value.rules.every(rule => record(rule) && hasExactKeys(rule, ['ruleCode', 'threshold', 'actual', 'deviation', 'unit'])
    && rule.ruleCode === (h.contract.discountLimitDefined ? 'sales.contract-discount-over-limit' : 'sales.contract-discount-limit-undefined')
    && (h.contract.amountDisclosure === 'Withheld' || !h.contract.discountLimitDefined
      ? rule.threshold === null && rule.actual === null && rule.deviation === null && rule.unit === null
      : [rule.threshold, rule.actual, rule.deviation].every(v => typeof v === 'string' && v.length <= 64 && v === v.trim()
        && /^[0-9]+(?:\.[0-9]+)?$/.test(v) && nativeAmount(v)) && rule.unit === '%'))
}
