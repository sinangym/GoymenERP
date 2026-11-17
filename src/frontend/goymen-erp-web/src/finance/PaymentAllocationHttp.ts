import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, devHeaders } from './http'
import type { CreateFinanceAllocationRequest, ReleaseFinanceAllocationRequest, FinanceAllocationCommandResult } from './allocationContract'
import { decodePaymentAllocation, decodePaymentAllocationCandidates, decodePaymentAllocationCommand,
  type PaymentAllocationCandidates } from './PaymentAllocationContract'
import { isPositiveInt64Text } from './contract'
export type PaymentAllocationRequest = { readonly operation: 'create'; readonly body: Omit<CreateFinanceAllocationRequest, 'sourceId' | 'expectedSourceVersion'> & { readonly paymentId: string; readonly expectedPaymentVersion: string } }
  | { readonly operation: 'release'; readonly body: ReleaseFinanceAllocationRequest }
const path = `${API_BASE}/finance/payment-allocations`
const headers = (companyId: string) => ({ ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' })
export async function fetchPaymentAllocationCandidates(companyId: string, paymentId: string, page: number,
  signal?: AbortSignal): Promise<ViewOutcome<PaymentAllocationCandidates>> {
  try {
    const response = await fetch(`${path}/candidates/${encodeURIComponent(paymentId)}?pageIndex=${page}`, { headers: headers(companyId), cache: 'no-store', signal })
    if (response.status !== 200) return { type: 'http', status: response.status }
    let body: unknown
    try { body = await response.json() } catch { return { type: 'malformed' } }
    if (signal?.aborted) return { type: 'fence-stale' }
    const answer = decodePaymentAllocationCandidates(body, companyId, paymentId, page)
    return answer.ok ? { type: 'ok-single', data: answer.value } : { type: 'malformed' }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export async function fetchPaymentAllocationReleaseVersion(companyId: string, allocationId: string,
  paymentId: string): Promise<ViewOutcome<{ readonly allocationVersion: string; readonly openItemVersion: string }>> {
  try {
    const response = await fetch(`${path}/${encodeURIComponent(allocationId)}`, { headers: headers(companyId), cache: 'no-store' })
    if (response.status !== 200) return { type: 'http', status: response.status }
    let body: unknown
    try { body = await response.json() } catch { return { type: 'malformed' } }
    if (typeof body !== 'object' || body === null || Object.keys(body).sort().join('|') !== 'allocation|openItemVersion') return { type: 'malformed' }
    const v = body as { allocation: unknown; openItemVersion: unknown }
    const answer = decodePaymentAllocation(v.allocation, companyId)
    if (!answer.ok || answer.value.id !== allocationId || answer.value.paymentId !== paymentId || answer.value.status !== 'Active'
      || !isPositiveInt64Text(v.openItemVersion)) return { type: 'malformed' }
    return { type: 'ok-single', data: { allocationVersion: answer.value.version, openItemVersion: v.openItemVersion } }
  } catch { return navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export type PaymentAllocationReply = { readonly kind: 'answered'; readonly result: FinanceAllocationCommandResult }
  | { readonly kind: 'refused'; readonly status: number } | { readonly kind: 'unknown' }
export async function sendPaymentAllocation(companyId: string, key: string, request: PaymentAllocationRequest): Promise<PaymentAllocationReply> {
  try {
    const response = await fetch(request.operation === 'create' ? path : `${path}/${encodeURIComponent(request.body.allocationId)}/release`, {
      method: 'POST', cache: 'no-store', headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(request.body),
    })
    if ([400, 401, 403, 404, 415].includes(response.status)) return { kind: 'refused', status: response.status }
    if (![200, 409, 422].includes(response.status)) return { kind: 'unknown' }
    const decoded = decodePaymentAllocationCommand(await response.json(), companyId)
    if (!decoded.ok || decoded.value.isReplay !== (response.headers.get('Idempotency-Replayed') === 'true')
      || (decoded.value.isReplay && decoded.value.originalCorrelationId !== response.headers.get('Idempotency-Original-Correlation-Id'))
      || decoded.value.outcome === 'Unavailable' || decoded.value.outcome === 'IdempotencyConflict'
      || (response.status === 200) !== (decoded.value.outcome === 'Succeeded')
      || (response.status === 422) !== (decoded.value.outcome === 'ValidationFailed')) return { kind: 'unknown' }
    if (decoded.value.outcome === 'Succeeded') {
      const item = decoded.value.resource
      if (item.id !== request.body.allocationId || (request.operation === 'release' ? item.status !== 'Released' || item.releaseReason !== request.body.reason
        : item.status !== 'Active' || item.sourceId !== request.body.paymentId || item.openItemId !== request.body.openItemId
          || item.amount !== request.body.amount || item.reason !== request.body.reason
          || (request.body.allocationAccountId !== undefined && (item.allocationAccountId !== request.body.allocationAccountId
            || item.allocationPartyId !== request.body.allocationPartyId)))) return { kind: 'unknown' }
    }
    return { kind: 'answered', result: decoded.value }
  } catch { return { kind: 'unknown' } }
}
