import {decodeCustomerReturnView, trimServerWhiteSpace, CUSTOMER_RETURN_REASON_MAX, CUSTOMER_RETURN_DOCUMENT_NO_MAX, type CustomerReturnView, type CustomerReturnAmounts} from '../sales/customerReturnContract'
import {isBoundUuid} from '../finance/contract'
import {hasExactKeys} from '../finance/wireShape'
import {salesRequestVersion, salesRequestIncrementable} from './workSalesRequestDecision'

export const SALES_CUSTOMER_RETURN_WORK_KIND = 'sales.customer-return.v1'
export const SALES_CUSTOMER_RETURN_WORK_CAPABILITY = 'sales.customer-return.work-decision'
export interface WorkSalesCustomerReturnPreview {readonly customerReturn: CustomerReturnView; readonly approvalSupported: boolean; readonly rejectionSupported: boolean}
export interface SalesCustomerReturnWorkCommand {readonly expectedVersion: string; readonly approve: boolean; readonly reason: string | null; readonly delegationId: string | null}
type Amount = (value: unknown, positive: boolean) => boolean
const record = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && v.length === 36 && v === v.trim() && isBoundUuid(v)
const same = (v: unknown, id: string) => typeof v === 'string' && v.toLowerCase() === id.toLowerCase()
const int32 = (v: number, min = 0) => Number.isInteger(v) && v >= min && v <= 2147483647
const actor = (v: string) => v === v.trim() && v.length > 0 && v.length <= 128
const utc = (v: string, date: (v: unknown) => boolean) => v === v.trim() && date(v.slice(0, 10))
  && /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
const amounts = (v: CustomerReturnAmounts, nativeAmount: Amount) => Object.entries(v).every(([key, text]) =>
  typeof text === 'string' && text === text.trim() && text.length <= 64
  && nativeAmount(key === 'driftAmount' && text.startsWith('-') ? text.slice(1) : text, false))

/** Native rejection checks the .NET-trimmed bound but hashes the ORIGINAL request. Never trim the frozen Work body. */
export const validSalesCustomerReturnReason = (approve: boolean, v: unknown): v is string | null => approve ? v === null
  : v === null || typeof v === 'string' && trimServerWhiteSpace(v).length <= CUSTOMER_RETURN_REASON_MAX

/** Reuse the full native 34-header/14-line/6-amount codec and its state/maker facts. Work adds CLR wire bounds, no formulas. */
export function decodeWorkSalesCustomerReturnPreview(v: unknown, company: string, id: string,
  date: (v: unknown) => boolean, nativeAmount: Amount): WorkSalesCustomerReturnPreview | null {
  if (!guid(company) || !guid(id) || !record(v) || !hasExactKeys(v, ['customerReturn', 'approvalSupported', 'rejectionSupported'])) return null
  const c = decodeCustomerReturnView(v.customerReturn, id)
  if (c === null || c.state === 'Draft' || !salesRequestVersion(c.version)
    || c.state === 'PendingApproval' && !salesRequestIncrementable(c.version)
    || ![c.id, c.originalInvoiceId, c.partyId, c.companyAccountId, c.currencyId].every(guid)
    || c.customerDocumentNo.length > CUSTOMER_RETURN_DOCUMENT_NO_MAX || c.shipmentId !== null && !guid(c.shipmentId) || !date(c.customerDocumentDate)
    || c.amountScale > 28 || !int32(c.amountScale) || c.functionalScale !== null && (!int32(c.functionalScale) || c.functionalScale > 28)
    || c.functionalCurrencyId !== null && !guid(c.functionalCurrencyId) || c.rate !== null && !nativeAmount(c.rate, true)
    || ![c.createdBy, c.submittedBy, c.approvedBy, c.finalizedBy, c.rejectedBy, c.cancelledBy].every(x => x === null || actor(x))
    || c.submittedBy === null || c.submittedAtUtc === null
    || ![c.createdAtUtc, c.submittedAtUtc, c.approvedAtUtc, c.finalizedAtUtc, c.rejectedAtUtc, c.cancelledAtUtc, c.updatedAtUtc].every(x => x === null || utc(x, date))
    || !amounts(c.totals, nativeAmount) || c.functionalTotals !== null && !amounts(c.functionalTotals, nativeAmount)
    || c.lines.some(l => !int32(l.lineNo, 1) || !int32(l.originalLineNo, 1)
      || ![l.originalInvoiceLineId, l.shipmentId, l.productId, l.unitId].every(guid) || !nativeAmount(l.quantity, true)
      || !nativeAmount(l.netUnitPrice, false) || !nativeAmount(l.vatPercent, false) || l.withholdingShare !== null && !nativeAmount(l.withholdingShare, false)
      || !amounts(l.amounts, nativeAmount) || l.functionalAmounts !== null && !amounts(l.functionalAmounts, nativeAmount))) return null
  const pending = c.state === 'PendingApproval'
  if (v.approvalSupported !== pending || v.rejectionSupported !== pending) return null
  return {customerReturn: c, approvalSupported: pending, rejectionSupported: pending}
}
export const isSalesCustomerReturnWorkCommand = (v: unknown): v is SalesCustomerReturnWorkCommand => record(v)
  && hasExactKeys(v, ['expectedVersion', 'approve', 'reason', 'delegationId']) && salesRequestIncrementable(v.expectedVersion)
  && typeof v.approve === 'boolean' && validSalesCustomerReturnReason(v.approve, v.reason) && (v.delegationId === null || guid(v.delegationId))
export function salesCustomerReturnWorkCommand(h: WorkSalesCustomerReturnPreview, approve: boolean, rawReason: string,
  delegationId: string | null): SalesCustomerReturnWorkCommand | null {
  if (!(approve ? h.approvalSupported : h.rejectionSupported)) return null
  const body = {expectedVersion: h.customerReturn.version, approve, reason: approve ? null : rawReason, delegationId}
  return isSalesCustomerReturnWorkCommand(body) ? body : null
}
export function validSalesCustomerReturnPreview(v: Record<string, unknown>, company: string, id: string,
  date: (v: unknown) => boolean, nativeAmount: Amount): boolean {
  const h = decodeWorkSalesCustomerReturnPreview(v.salesCustomerReturn, company, id, date, nativeAmount)
  if (h === null || !hasExactKeys(v, ['companyId', 'kind', 'itemId', 'documentLabel', 'sourceVersion', 'pending', 'approvalReasonRequired',
    'references', 'rules', 'approvalSupported', 'rejectionSupported', 'salesCustomerReturn'])
    || !same(v.companyId, company) || !same(v.itemId, id) || v.kind !== SALES_CUSTOMER_RETURN_WORK_KIND
    || v.documentLabel !== h.customerReturn.customerDocumentNo.trim() || v.sourceVersion !== h.customerReturn.version
    || v.pending !== (h.customerReturn.state === 'PendingApproval') || v.approvalReasonRequired !== false
    || v.approvalSupported !== h.approvalSupported || v.rejectionSupported !== h.rejectionSupported
    || !Array.isArray(v.references) || v.references.length !== 2 || !Array.isArray(v.rules) || v.rules.length !== 1) return false
  const rule = v.rules[0], own = v.references[0], invoice = v.references[1], c = h.customerReturn
  return record(rule) && hasExactKeys(rule, ['ruleCode', 'threshold', 'actual', 'deviation', 'unit'])
    && rule.ruleCode === 'sales.customer-return-approval-required' && [rule.threshold, rule.actual, rule.deviation, rule.unit].every(x => x === null)
    && record(own) && hasExactKeys(own, ['role', 'label', 'recordId', 'link']) && own.role === 'sales-customer-return'
    && own.label === c.customerDocumentNo.trim() && same(own.recordId, id) && typeof own.link === 'string' && own.link.toLowerCase() === `/satis/iadeler/${id.toLowerCase()}`
    && record(invoice) && hasExactKeys(invoice, ['role', 'label', 'recordId', 'link']) && invoice.role === 'sales-invoice'
    && invoice.label === c.originalDocumentNo.trim() && same(invoice.recordId, c.originalInvoiceId) && invoice.link === '/satis/faturalar'
}
