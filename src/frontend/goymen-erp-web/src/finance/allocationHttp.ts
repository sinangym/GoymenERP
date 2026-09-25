import { classifyAllocationReply } from './allocationCommand'
import type { AllocationCommandRequest, AllocationSettlement } from './allocationCommand'
import { decodeFinanceAllocationCommandResult } from './allocationContract'
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

function malformed(snapshot: FinanceAllocationWireSnapshot): FinanceAllocationHttpResult {
  return {
    kind: 'settlement',
    settlement: classifyAllocationReply({ kind: 'body', body: undefined }, snapshot.binding.scope, snapshot.request),
  }
}

function noReply(snapshot: FinanceAllocationWireSnapshot): FinanceAllocationHttpResult {
  return {
    kind: 'settlement',
    settlement: classifyAllocationReply({ kind: 'no-reply' }, snapshot.binding.scope, snapshot.request),
  }
}

export async function sendFinanceAllocationCommand(
  snapshot: FinanceAllocationWireSnapshot,
  signal?: AbortSignal,
): Promise<FinanceAllocationHttpResult> {
  const { key, binding, request } = snapshot
  if (
    key.trim().length === 0
    || !isBoundUuid(binding.scope.companyId)
    || (request.kind === 'release' && !isBoundUuid(request.body.allocationId))
  ) {
    return malformed(snapshot)
  }

  const path = request.kind === 'create'
    ? '/finance/allocations'
    : `/finance/allocations/${encodeURIComponent(request.body.allocationId)}/release`

  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'X-Company-Id': binding.scope.companyId,
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
  if (response.status >= 500) return noReply(snapshot)
  if (response.status !== 200 && response.status !== 409 && response.status !== 422) {
    return malformed(snapshot)
  }

  let body: unknown
  try {
    body = await response.json()
  } catch {
    return malformed(snapshot)
  }

  const decoded = decodeFinanceAllocationCommandResult(body)
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
    settlement: classifyAllocationReply({ kind: 'body', body }, binding.scope, request),
  }
}
