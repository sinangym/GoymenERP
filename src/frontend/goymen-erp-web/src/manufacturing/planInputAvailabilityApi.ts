import { MANUFACTURING_BASE, devHeaders } from './api'
import type { ViewOutcome } from '../shell/uiState'

export interface PlanInputAvailabilityLine {
  readonly labelId: string
  readonly productId: string
  readonly labelVersion: string
  readonly requestedKg: string
  readonly physicalKg: string
  readonly blockedKg: string
  readonly allocatedKg: string
  readonly freeKg: string
  readonly missingKg: string
  readonly freePositionCount: number
}
export interface PlanInputAvailability {
  readonly contractVersion: 1
  readonly companyId: string
  readonly planId: string
  readonly planVersion: number
  readonly inputs: readonly PlanInputAvailabilityLine[]
  readonly cancelledOrders: readonly { readonly orderId: string; readonly planVersionAfterCancel: number }[]
  readonly readAtUtc: string
}
const GUID = /^(?!00000000-0000-0000-0000-000000000000)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const KG = /^(0|[1-9][0-9]*)(\.[0-9]{1,3})?$/
const VERSION = /^[1-9][0-9]*$/
function object(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
function guid(value: unknown): value is string { return typeof value === 'string' && GUID.test(value) }
function positive(value: unknown): value is number { return Number.isSafeInteger(value) && (value as number) > 0 }

// Key sets mirror PlanInputAvailabilityViewV1 / PlanInputAvailabilityLineV1 / PlanCancelledOrderReferenceV1 one-to-one.
function exact(value: Record<string, unknown>, names: readonly string[]): boolean {
  return Object.keys(value).length === names.length && names.every((key) => Object.hasOwn(value, key))
}
const VIEW_KEYS = ['contractVersion', 'companyId', 'planId', 'planVersion', 'inputs', 'cancelledOrders', 'readAtUtc'] as const
const LINE_KEYS = ['labelId', 'productId', 'labelVersion', 'requestedKg', 'physicalKg', 'blockedKg', 'allocatedKg', 'freeKg', 'missingKg',
  'freePositionCount'] as const
const CANCELLED_ORDER_KEYS = ['orderId', 'planVersionAfterCancel'] as const

/** All kg fields are server text. The decoder never subtracts or rounds quantities. */
export function decodePlanInputAvailability(value: unknown): PlanInputAvailability | null {
  if (!object(value) || !exact(value, VIEW_KEYS) || value.contractVersion !== 1 || !guid(value.companyId) || !guid(value.planId)
    || !positive(value.planVersion) || !Array.isArray(value.inputs) || value.inputs.length === 0
    || !Array.isArray(value.cancelledOrders) || typeof value.readAtUtc !== 'string'
    || !Number.isFinite(Date.parse(value.readAtUtc))) return null
  const labels = new Set<string>()
  for (const line of value.inputs) {
    if (!object(line) || !exact(line, LINE_KEYS) || !guid(line.labelId) || labels.has(line.labelId.toLowerCase()) || !guid(line.productId)
      || typeof line.labelVersion !== 'string' || !VERSION.test(line.labelVersion)
      || !Number.isSafeInteger(line.freePositionCount) || (line.freePositionCount as number) < 0) return null
    if (['requestedKg', 'physicalKg', 'blockedKg', 'allocatedKg', 'freeKg', 'missingKg']
      .some((key) => typeof line[key] !== 'string' || !KG.test(line[key] as string))) return null
    labels.add(line.labelId.toLowerCase())
  }
  const orders = new Set<string>()
  let previous = 0
  for (const entry of value.cancelledOrders) {
    if (!object(entry) || !exact(entry, CANCELLED_ORDER_KEYS) || !guid(entry.orderId) || orders.has(entry.orderId.toLowerCase())
      || !positive(entry.planVersionAfterCancel) || entry.planVersionAfterCancel <= previous
      || entry.planVersionAfterCancel > value.planVersion) return null
    orders.add(entry.orderId.toLowerCase()); previous = entry.planVersionAfterCancel
  }
  return value as unknown as PlanInputAvailability
}

export async function fetchPlanInputAvailability(companyId: string, planId: string, planVersion: number,
  signal?: AbortSignal): Promise<ViewOutcome<PlanInputAvailability>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/plans/${encodeURIComponent(planId)}/input-availability`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId }, cache: 'no-store', signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try { body = await response.json() } catch { if (signal?.aborted) return { type: 'fence-stale' } }
  if (signal?.aborted) return { type: 'fence-stale' }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const value = decodePlanInputAvailability(body)
  if (value === null || value.companyId.toLowerCase() !== companyId.toLowerCase()
    || value.planId.toLowerCase() !== planId.toLowerCase()) return { type: 'malformed' }
  if (value.planVersion !== planVersion) return { type: 'http', status: 409 }
  return { type: 'ok-single', data: value }
}
