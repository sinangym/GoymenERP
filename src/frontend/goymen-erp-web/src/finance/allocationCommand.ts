import {
  decodeFinanceAllocationCommandResult,
  type CreateFinanceAllocationRequest,
  type FinanceAllocation,
  type ReleaseFinanceAllocationRequest,
} from './allocationContract'
import type { ValidationIssue } from './contract'
import type { FinanceScope, PortReply } from './port'
import type { AttemptBinding } from './receiptCommand'

export type AllocationCommandRequest =
  | { readonly operation: 'allocation-create'; readonly body: CreateFinanceAllocationRequest }
  | { readonly operation: 'allocation-release'; readonly body: ReleaseFinanceAllocationRequest }

export type AllocationSettlement =
  | { readonly type: 'succeeded'; readonly resource: FinanceAllocation; readonly replayed: boolean }
  | {
      readonly type: 'refused'
      readonly reason: 'denied' | 'validation' | 'version-conflict'
      readonly issues: readonly ValidationIssue[]
      readonly replayed: boolean
    }
  | { readonly type: 'unknown'; readonly reason: 'no-reply' | 'malformed' | 'unavailable' | 'idempotency-conflict' }

function resourceAnswers(request: AllocationCommandRequest, resource: FinanceAllocation): boolean {
  if (resource.id !== request.body.allocationId) return false
  if (request.operation === 'allocation-release') return resource.status === 'Released'
  return resource.status === 'Active' && resource.sourceId === request.body.sourceId &&
    resource.openItemId === request.body.openItemId
}

export function classifyAllocationReply(
  reply: PortReply,
  scope: FinanceScope,
  request: AllocationCommandRequest,
): AllocationSettlement {
  if (reply.kind === 'no-reply') return { type: 'unknown', reason: 'no-reply' }
  const decoded = decodeFinanceAllocationCommandResult(reply.body, scope.companyId)
  if (!decoded.ok) return { type: 'unknown', reason: 'malformed' }
  const result = decoded.value
  switch (result.outcome) {
    case 'Succeeded':
      return resourceAnswers(request, result.resource)
        ? { type: 'succeeded', resource: result.resource, replayed: result.isReplay }
        : { type: 'unknown', reason: 'malformed' }
    case 'Unavailable':
      return { type: 'unknown', reason: 'unavailable' }
    case 'IdempotencyConflict':
      return { type: 'unknown', reason: 'idempotency-conflict' }
    case 'Denied':
      return { type: 'refused', reason: 'denied', issues: result.issues, replayed: result.isReplay }
    case 'ValidationFailed':
      return { type: 'refused', reason: 'validation', issues: result.issues, replayed: result.isReplay }
    case 'VersionConflict':
      return { type: 'refused', reason: 'version-conflict', issues: result.issues, replayed: result.isReplay }
  }
}

function stable(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stable)
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(Object.keys(value as Record<string, unknown>).sort()
      .map((key) => [key, stable((value as Record<string, unknown>)[key])]))
  }
  return value
}

export function allocationAttemptIdentity(binding: AttemptBinding, request: AllocationCommandRequest): string {
  return JSON.stringify(stable({ binding, request }))
}

export function allocationAttemptTarget(request: AllocationCommandRequest): string {
  return request.body.allocationId
}
