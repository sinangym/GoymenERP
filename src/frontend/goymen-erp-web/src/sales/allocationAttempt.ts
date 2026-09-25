import type { AllocationCommandSnapshot } from './allocationApi'

export type AllocationAttemptKind = 'create' | 'release'

export interface AllocationAttemptScope {
  readonly companyId: string
  readonly salesOrderId: string
  readonly salesOrderLineId: string
  readonly kind: AllocationAttemptKind
  readonly allocationId?: string
}

interface StoredAttempt<T> extends AllocationCommandSnapshot<T> {
  readonly schema: 1
  readonly scope: AllocationAttemptScope
}

const PREFIX = 'goymen:sales:allocation-attempt:v1:'

const keyOf = (scope: AllocationAttemptScope) => PREFIX + [scope.companyId, scope.salesOrderId,
  scope.salesOrderLineId, scope.kind, scope.allocationId ?? '-'].map(encodeURIComponent).join(':')

const uuid = () => globalThis.crypto.randomUUID()

export function loadAllocationAttempt<T>(scope: AllocationAttemptScope): StoredAttempt<T> | null {
  try {
    const raw = window.localStorage.getItem(keyOf(scope))
    if (raw === null) return null
    const value = JSON.parse(raw) as Partial<StoredAttempt<T>>
    if (value.schema !== 1 || JSON.stringify(value.scope) !== JSON.stringify(scope)
      || typeof value.idempotencyKey !== 'string' || typeof value.correlationId !== 'string'
      || value.body === undefined) return null
    return value as StoredAttempt<T>
  } catch {
    return null
  }
}

export function beginAllocationAttempt<T>(scope: AllocationAttemptScope, body: T): StoredAttempt<T> {
  const existing = loadAllocationAttempt<T>(scope)
  if (existing !== null) return existing
  const snapshot: StoredAttempt<T> = {
    schema: 1,
    scope,
    idempotencyKey: uuid(),
    correlationId: uuid().replaceAll('-', ''),
    body,
  }
  try {
    window.localStorage.setItem(keyOf(scope), JSON.stringify(snapshot))
  } catch {
    // The in-memory snapshot remains immutable for this mounted attempt.
  }
  return snapshot
}

export function finishAllocationAttempt(scope: AllocationAttemptScope, idempotencyKey: string): void {
  const existing = loadAllocationAttempt(scope)
  if (existing?.idempotencyKey !== idempotencyKey) return
  try {
    window.localStorage.removeItem(keyOf(scope))
  } catch {
    // A settled request may leave harmless retry metadata when storage is unavailable.
  }
}

export function sameAttemptBody(left: unknown, right: unknown): boolean {
  return JSON.stringify(left) === JSON.stringify(right)
}
