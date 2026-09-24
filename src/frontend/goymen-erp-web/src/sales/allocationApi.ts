import type { ViewOutcome } from '../shell/uiState'
import {
  PHYSICAL_ALLOCATIONS_PATH,
  decodePhysicalAllocation,
  decodePhysicalAllocationPage,
  problemCode,
  type AllocationScope,
  type CreatePhysicalAllocationBody,
  type PhysicalAllocation,
  type PhysicalAllocationPage,
  type ReleasePhysicalAllocationBody,
} from './allocationContract'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

type HttpRaw = { readonly status: number; readonly body: unknown; readonly replayed: boolean }
type Raw = HttpRaw
  | { readonly failure: 'aborted' | 'offline' | 'unreachable' }

export interface AllocationCommandSnapshot<T> {
  readonly idempotencyKey: string
  readonly correlationId: string
  readonly body: T
}

export type AllocationMutationOutcome =
  | { readonly type: 'recorded'; readonly allocation: PhysicalAllocation; readonly replayed: boolean }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'unreachable' | 'malformed' }
  | { readonly type: 'aborted' }

function headers(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

async function request(path: string, init: RequestInit, signal?: AbortSignal): Promise<Raw> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { ...init, signal })
  } catch {
    if (signal?.aborted) return { failure: 'aborted' }
    if (!navigator.onLine) return { failure: 'offline' }
    return { failure: 'unreachable' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    body = null
  }
  return { status: response.status, body, replayed: response.headers.get('Idempotency-Replayed') === 'true' }
}

export async function fetchPhysicalAllocations(
  scope: AllocationScope,
  signal?: AbortSignal,
): Promise<ViewOutcome<PhysicalAllocationPage>> {
  const query = new URLSearchParams({
    salesOrderId: scope.salesOrderId,
    salesOrderLineId: scope.salesOrderLineId,
    limit: '100',
  })
  const raw = await request(`${PHYSICAL_ALLOCATIONS_PATH}?${query}`, { headers: headers(scope.companyId) }, signal)
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'fence-stale' }
    return { type: raw.failure === 'offline' ? 'offline' : 'network-error' }
  }
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const page = decodePhysicalAllocationPage(raw.body, scope)
  return page === null ? { type: 'malformed' } : { type: 'ok-single', data: page }
}

async function command<T>(
  path: string,
  scope: AllocationScope,
  snapshot: AllocationCommandSnapshot<T>,
  accepted: (raw: HttpRaw) => boolean,
  expectedAllocationId: string,
): Promise<AllocationMutationOutcome> {
  const raw = await request(path, {
    method: 'POST',
    headers: {
      ...headers(scope.companyId),
      'Content-Type': 'application/json',
      'Idempotency-Key': snapshot.idempotencyKey,
      'X-Correlation-Id': snapshot.correlationId,
    },
    body: JSON.stringify(snapshot.body),
  })
  if ('failure' in raw) {
    return raw.failure === 'aborted' ? { type: 'aborted' } : { type: 'unknown', reason: raw.failure }
  }
  if (!accepted(raw)) return raw.status >= 500
    ? { type: 'unknown', reason: 'unreachable' }
    : { type: 'http', status: raw.status, code: problemCode(raw.body) }
  const allocation = decodePhysicalAllocation(raw.body, scope)
  if (allocation === null || allocation.allocationId !== expectedAllocationId) {
    return { type: 'unknown', reason: 'malformed' }
  }
  return { type: 'recorded', allocation, replayed: raw.replayed }
}

export function createPhysicalAllocation(
  scope: AllocationScope,
  snapshot: AllocationCommandSnapshot<CreatePhysicalAllocationBody>,
): Promise<AllocationMutationOutcome> {
  return command(PHYSICAL_ALLOCATIONS_PATH, scope, snapshot,
    (raw) => raw.status === 201 && !raw.replayed || raw.status === 200 && raw.replayed,
    snapshot.body.allocationId)
}

export function releasePhysicalAllocation(
  scope: AllocationScope,
  allocationId: string,
  snapshot: AllocationCommandSnapshot<ReleasePhysicalAllocationBody>,
): Promise<AllocationMutationOutcome> {
  return command(`${PHYSICAL_ALLOCATIONS_PATH}/${encodeURIComponent(allocationId)}/release`, scope, snapshot,
    (raw) => raw.status === 200,
    allocationId)
}
