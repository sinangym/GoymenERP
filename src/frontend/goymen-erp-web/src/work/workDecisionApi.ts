import { API_BASE, devHeaders } from '../finance/http'
import { isBoundUuid, isPositiveInt64Text } from '../finance/contract'
import { hasExactKeys, replayHeadersAgree } from '../finance/wireShape'
import { readJson, type CommandOutcome } from '../settings/discountLimitApi'

export const WORK_DECISION_KINDS = ['inventory.opening-cost.v1', 'finance.cash-account.v1',
  'finance.cash-account-passivation.v1', 'finance.cash-account-reactivation.v1','sales.order-draft.v1','sales.invoice.v1','sales.subcontract-invoice.v1','purchasing.request.v1'] as const
export type WorkDecisionKind = typeof WORK_DECISION_KINDS[number]
export const isSalesWorkDecisionKind = (kind: WorkDecisionKind) => kind === 'sales.order-draft.v1' || kind === 'sales.invoice.v1' || kind === 'sales.subcontract-invoice.v1'
/** Native Sales and PurchaseRequest reason bounds; optional rejection remains Sales-only. */
export const workDecisionReasonLimit = (kind: WorkDecisionKind) => isSalesWorkDecisionKind(kind) || kind === 'purchasing.request.v1' ? 4000 : 500
export const isWorkDecisionKind = (value: string): value is WorkDecisionKind => WORK_DECISION_KINDS.some(kind => kind === value)
export interface WorkDecisionRequest { readonly expectedVersion: string; readonly approve: boolean; readonly reason: string | null; readonly delegationId: string | null }
export interface WorkDecisionPreview {
  readonly companyId: string; readonly kind: WorkDecisionKind; readonly itemId: string; readonly documentLabel: string
  readonly sourceVersion: string; readonly pending: boolean; readonly approvalReasonRequired: boolean
  readonly references: readonly { readonly role: string; readonly label: string; readonly recordId: string | null; readonly link: string | null }[]
  readonly rules: readonly { readonly ruleCode: string; readonly threshold: string | null; readonly actual: string | null; readonly deviation: string | null; readonly unit: string | null }[]
}
export interface WorkDecisionResult { readonly companyId: string; readonly kind: WorkDecisionKind; readonly itemId: string; readonly state: string; readonly replayed: boolean; readonly originalCorrelationId: string }
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const nullableText = (value: unknown) => value === null || typeof value === 'string'
const same = (value: unknown, expected: string) => typeof value === 'string' && value.toLowerCase() === expected.toLowerCase()
const path = (kind: WorkDecisionKind, id: string) => `/work/decisions/${encodeURIComponent(kind)}/${encodeURIComponent(id)}`
export const isWorkDecisionRequest = (value: unknown): value is WorkDecisionRequest => record(value)
  && hasExactKeys(value, ['expectedVersion', 'approve', 'reason', 'delegationId']) && text(value.expectedVersion)
  && typeof value.approve === 'boolean' && nullableText(value.reason) && (value.delegationId === null || isBoundUuid(value.delegationId))
export function decodeWorkDecisionPreview(value: unknown, company: string, kind: WorkDecisionKind, id: string): WorkDecisionPreview | null {
  if (!record(value) || !hasExactKeys(value, ['companyId', 'kind', 'itemId', 'documentLabel', 'sourceVersion', 'pending', 'approvalReasonRequired', 'references', 'rules'])
    || !same(value.companyId, company) || value.kind !== kind || !same(value.itemId, id) || !text(value.documentLabel)
    || (kind === 'inventory.opening-cost.v1' ? value.sourceVersion !== `submission/${id}` : !isPositiveInt64Text(value.sourceVersion))
    || typeof value.pending !== 'boolean' || value.approvalReasonRequired !== (kind === 'inventory.opening-cost.v1')
    || !Array.isArray(value.references) || value.references.length > 12 || !value.references.every(ref => record(ref)
      && hasExactKeys(ref, ['role', 'label', 'recordId', 'link']) && text(ref.role) && text(ref.label)
      && (ref.recordId === null || isBoundUuid(ref.recordId)) && nullableText(ref.link))
    || !Array.isArray(value.rules) || value.rules.length > 8 || !value.rules.every(rule => record(rule)
      && hasExactKeys(rule, ['ruleCode', 'threshold', 'actual', 'deviation', 'unit']) && text(rule.ruleCode)
      && [rule.threshold, rule.actual, rule.deviation, rule.unit].every(nullableText))) return null
  return value as unknown as WorkDecisionPreview
}
export const fetchWorkDecision = (company: string, kind: WorkDecisionKind, id: string, signal?: AbortSignal) =>
  readJson(path(kind, id), company, value => decodeWorkDecisionPreview(value, company, kind, id), signal)
export async function sendWorkDecision(company: string, kind: WorkDecisionKind, id: string, request: WorkDecisionRequest, key: string): Promise<CommandOutcome<WorkDecisionResult>> {
  let response: Response
  try { response = await fetch(API_BASE + path(kind, id), { method: 'POST', headers: { ...devHeaders(),
    'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(request) }) }
  catch { return { type: 'unknown', reason: 'network-error' } }
  const body: unknown = await response.json().catch(() => null)
  if (response.status === 200) {
    const state = kind === 'finance.cash-account-passivation.v1' ? request.approve ? 'Passive' : 'Approved'
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
    return record(body) && hasExactKeys(body, ['companyId', 'kind', 'itemId', 'state', 'replayed', 'originalCorrelationId'])
      && same(body.companyId, company) && body.kind === kind && same(body.itemId, id) && (body.state === state || currentPurchaseRejection || currentInvoiceApproval)
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
