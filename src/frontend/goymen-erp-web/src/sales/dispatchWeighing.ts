import { headers, readFailure, request } from './api'
import { isReferenceId } from './wirePrimitives'
import type { ViewOutcome } from '../shell/uiState'

export interface DispatchWeighing {
  readonly dispatchNoteId: string; readonly companyId: string; readonly state: 'Recorded' | 'NotRecorded'
  readonly recordId: string | null; readonly theoreticalKg: string | null; readonly scaleKg: string | null
  readonly differenceKg: string | null; readonly totalExcessKg: string | null
  readonly bandLevel: 'NotEvaluated' | 'None' | 'Warning' | 'Approval' | 'Block' | null
  readonly decision: 'NoExcess' | 'WithinRate' | 'ApprovalRequired' | null
  readonly recordState: 'Recorded' | 'PendingApproval' | 'Approved' | null
  readonly saleMayProceed: boolean; readonly invoiceWait: 'NotRecorded' | 'PendingApproval' | 'KgTransferPending' | null
  readonly version: number | null
}
const fields = ['dispatchNoteId', 'companyId', 'state', 'recordId', 'theoreticalKg', 'scaleKg', 'differenceKg',
  'totalExcessKg', 'bandLevel', 'decision', 'recordState', 'saleMayProceed', 'invoiceWait', 'version']
const kg = (v: unknown): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,14})(?:\.\d{1,3})?$/.test(v)
const signedKg = (v: unknown) => kg(v) || typeof v === 'string' && v.startsWith('-') && kg(v.slice(1))

/** Preserve owner kilograms as text; never calculate a difference, excess, band or approval on the client. */
export function decodeDispatchWeighing(value: unknown): DispatchWeighing | null {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return null
  const v = value as Record<string, unknown>
  if (Object.keys(v).length !== fields.length || fields.some(f => !Object.hasOwn(v, f))
    || !isReferenceId(v.dispatchNoteId) || !isReferenceId(v.companyId) || typeof v.saleMayProceed !== 'boolean') return null
  if (v.state === 'NotRecorded') {
    if (['recordId', 'theoreticalKg', 'scaleKg', 'differenceKg', 'totalExcessKg', 'bandLevel', 'decision', 'recordState', 'version'].some(f => v[f] !== null)
      || v.saleMayProceed || v.invoiceWait !== 'NotRecorded') return null
  } else if (v.state === 'Recorded') {
    if (!isReferenceId(v.recordId) || !kg(v.theoreticalKg) || !kg(v.scaleKg) || !signedKg(v.differenceKg) || !kg(v.totalExcessKg)
      || !['NotEvaluated', 'None', 'Warning', 'Approval', 'Block'].includes(String(v.bandLevel))
      || !['NoExcess', 'WithinRate', 'ApprovalRequired'].includes(String(v.decision))
      || !['Recorded', 'PendingApproval', 'Approved'].includes(String(v.recordState))
      || !(v.invoiceWait === null || ['PendingApproval', 'KgTransferPending'].includes(String(v.invoiceWait)))
      || typeof v.version !== 'number' || !Number.isSafeInteger(v.version) || v.version < 1 || v.version > 2147483647
      || v.recordState === 'PendingApproval' && (v.saleMayProceed || v.invoiceWait !== 'PendingApproval')) return null
  } else return null
  return value as DispatchWeighing
}

export async function fetchDispatchWeighing(id: string, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<DispatchWeighing>> {
  const raw = await request(`/sales/dispatches/${encodeURIComponent(id)}/weighing-difference`,
    { headers: headers(companyId), cache: 'no-store' }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const value = decodeDispatchWeighing(raw.body)
  return value === null || value.companyId !== companyId || value.dispatchNoteId !== id
    ? { type: 'malformed' } : { type: 'ok-single', data: value }
}
