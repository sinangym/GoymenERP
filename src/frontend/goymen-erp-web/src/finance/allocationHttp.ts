import { classifyAllocationReply } from './allocationCommand'
import type { AllocationCommandRequest, AllocationSettlement } from './allocationCommand'
import {
  decodeFinanceAllocationCommandResult,
  decodeFinanceAllocationDetail,
  decodeFinanceAllocationPage,
  type FinanceAllocationDetail,
  type FinanceAllocationPage,
} from './allocationContract'
import { isBoundUuid } from './contract'
import { API_BASE, devHeaders } from './http'
import type { AttemptBinding } from './receiptCommand'

export interface FinanceAllocationWireSnapshot {
  readonly key: string
  readonly binding: AttemptBinding
  readonly request: AllocationCommandRequest
}

export type FinanceAllocationHttpResult =
  | { readonly kind: 'authorization'; readonly status: 401 | 403 }
  | { readonly kind: 'http-rejection'; readonly status: 400 | 415 }
  | { readonly kind: 'settlement'; readonly settlement: AllocationSettlement }

export type FinanceAllocationReadHttpResult<T> =
  | { readonly kind: 'found'; readonly resource: T }
  | { readonly kind: 'authorization'; readonly status: 401 | 403 }
  | { readonly kind: 'not-found' | 'unavailable' | 'malformed' | 'no-reply' }

async function readAllocation<T>(
  path: string,
  binding: AttemptBinding,
  decode: (value: unknown, companyId: string) => { readonly ok: false } | { readonly ok: true; readonly value: T },
  signal?: AbortSignal,
): Promise<FinanceAllocationReadHttpResult<T>> {
  if (!isBoundUuid(binding.companyId)) return { kind: 'malformed' }
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'GET', headers: { ...devHeaders(), 'X-Company-Id': binding.companyId, Accept: 'application/json' }, signal,
    })
  } catch {
    return { kind: 'no-reply' }
  }
  if (response.status === 401 || response.status === 403) return { kind: 'authorization', status: response.status }
  if (response.status === 404) return { kind: 'not-found' }
  if (response.status === 503) return { kind: 'unavailable' }
  if (response.status !== 200) return { kind: 'malformed' }
  try {
    const decoded = decode(await response.json(), binding.companyId)
    return decoded.ok ? { kind: 'found', resource: decoded.value } : { kind: 'malformed' }
  } catch (error) {
    return signal?.aborted || isUnresolvedBodyRead(error) ? { kind: 'no-reply' } : { kind: 'malformed' }
  }
}

export const readFinanceAllocations = (binding: AttemptBinding, signal?: AbortSignal) =>
  readAllocation<FinanceAllocationPage>('/finance/allocations', binding, decodeFinanceAllocationPage, signal)

export const readFinanceAllocation = async (
  binding: AttemptBinding,
  allocationId: string,
  signal?: AbortSignal,
): Promise<FinanceAllocationReadHttpResult<FinanceAllocationDetail>> => {
  if (!isBoundUuid(allocationId)) return { kind: 'malformed' }
  const result = await readAllocation<FinanceAllocationDetail>(
    `/finance/allocations/${encodeURIComponent(allocationId)}`,
    binding,
    decodeFinanceAllocationDetail,
    signal,
  )
  return result.kind === 'found' && result.resource.allocation.id !== allocationId
    ? { kind: 'malformed' }
    : result
}

function malformed(snapshot: FinanceAllocationWireSnapshot): FinanceAllocationHttpResult {
  return {
    kind: 'settlement',
    settlement: classifyAllocationReply(
      { kind: 'body', body: undefined },
      { companyId: snapshot.binding.companyId },
      snapshot.request,
    ),
  }
}

function noReply(snapshot: FinanceAllocationWireSnapshot): FinanceAllocationHttpResult {
  return {
    kind: 'settlement',
    settlement: classifyAllocationReply(
      { kind: 'no-reply' },
      { companyId: snapshot.binding.companyId },
      snapshot.request,
    ),
  }
}

function unavailable(snapshot: FinanceAllocationWireSnapshot): FinanceAllocationHttpResult {
  return {
    kind: 'settlement',
    settlement: classifyAllocationReply(
      {
        kind: 'body',
        body: {
          outcome: 'Unavailable',
          resource: null,
          isReplay: false,
          originalCorrelationId: '',
          issues: [],
        },
      },
      { companyId: snapshot.binding.companyId },
      snapshot.request,
    ),
  }
}

function isUnresolvedBodyRead(error: unknown): boolean {
  if (error instanceof TypeError) return true
  return typeof error === 'object'
    && error !== null
    && 'name' in error
    && error.name === 'AbortError'
}

export async function sendFinanceAllocationCommand(
  snapshot: FinanceAllocationWireSnapshot,
  signal?: AbortSignal,
): Promise<FinanceAllocationHttpResult> {
  const { key, binding, request } = snapshot
  if (
    key.trim().length === 0
    || !isBoundUuid(binding.companyId)
    || (request.operation === 'allocation-release' && !isBoundUuid(request.body.allocationId))
  ) {
    return malformed(snapshot)
  }

  const path = request.operation === 'allocation-create'
    ? '/finance/allocations'
    : `/finance/allocations/${encodeURIComponent(request.body.allocationId)}/release`

  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'X-Company-Id': binding.companyId,
        'Idempotency-Key': key,
        'Content-Type': 'application/json',
        Accept: 'application/json',
      },
      body: JSON.stringify(request.body),
      signal,
    })
  } catch {
    return noReply(snapshot)
  }

  if (response.status === 401 || response.status === 403) {
    return { kind: 'authorization', status: response.status }
  }
  if (response.status === 400 || response.status === 415) {
    return { kind: 'http-rejection', status: response.status }
  }
  if (response.status === 503) return unavailable(snapshot)
  if (response.status >= 500) return malformed(snapshot)
  if (response.status !== 200 && response.status !== 409 && response.status !== 422) {
    return malformed(snapshot)
  }

  let body: unknown
  try {
    body = await response.json()
  } catch (error) {
    return signal?.aborted || isUnresolvedBodyRead(error) ? noReply(snapshot) : malformed(snapshot)
  }

  const decoded = decodeFinanceAllocationCommandResult(body, binding.companyId)
  if (!decoded.ok) return malformed(snapshot)
  const expectedOutcome = response.status === 200
    ? 'Succeeded'
    : response.status === 409
      ? ['VersionConflict', 'IdempotencyConflict']
      : 'ValidationFailed'
  const coherent = Array.isArray(expectedOutcome)
    ? expectedOutcome.includes(decoded.value.outcome)
    : decoded.value.outcome === expectedOutcome
  if (!coherent) return malformed(snapshot)

  return {
    kind: 'settlement',
    settlement: classifyAllocationReply({ kind: 'body', body }, { companyId: binding.companyId }, request),
  }
}
