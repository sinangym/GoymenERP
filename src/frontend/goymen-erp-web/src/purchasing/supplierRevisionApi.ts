import type { ViewOutcome } from '../shell/uiState'
import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import { decodePurchaseOrderCommandResponse, type CreatePurchaseOrderLinePriceRequest } from './contract'

export interface SupplierRevisionHistory {
  readonly companyId: string; readonly orderId: string; readonly version: string; readonly revision: number
  readonly revisions: readonly { readonly revision: number; readonly orderVersionAfter: string; readonly previousSupplierPartyId: string
    readonly supplierPartyId: string; readonly preparedBy: string; readonly previousApprovedBy: string; readonly previousApprovedAtUtc: string
    readonly reason: string; readonly recordedAtUtc: string }[]
}
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const version = /^[1-9][0-9]*$/
function object(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value) }
function text(value: unknown): value is string { return typeof value === 'string' && value.length > 0 }
/** Exactly the required keys plus, at most, the optional ones: an unknown or a missing key fails the decoder closed. */
function exact(value: Record<string, unknown>, required: readonly string[], optional: readonly string[] = []): boolean {
  return required.every(key => Object.hasOwn(value, key)) && Object.keys(value).every(key => required.includes(key) || optional.includes(key))
}
const HISTORY_FIELDS = ['companyId', 'orderId', 'version', 'revision', 'revisions'] as const
const REVISION_FIELDS = ['revision', 'orderVersionAfter', 'previousSupplierPartyId', 'supplierPartyId', 'preparedBy', 'previousApprovedBy',
  'previousApprovedAtUtc', 'reason', 'recordedAtUtc'] as const
const ATTEMPT_FIELDS = ['actorId', 'companyId', 'orderId', 'idempotencyKey', 'command'] as const
const SUPPLIER_COMMAND_FIELDS = ['kind', 'expectedVersion', 'supplierPartyId', 'companyAccountId', 'reason'] as const
const PRICE_COMMAND_FIELDS = ['kind', 'expectedVersion', 'lineId', 'price'] as const
function timestamp(value: unknown): boolean { return text(value) && /^\d{4}-\d{2}-\d{2}T/.test(value) && Number.isFinite(Date.parse(value)) }
export function decodeSupplierRevisionHistory(value: unknown, companyId: string, orderId: string): SupplierRevisionHistory | null {
  if (!object(value) || !exact(value, HISTORY_FIELDS) || value.companyId !== companyId || value.orderId !== orderId || !text(value.version) || !version.test(value.version)
    || !Number.isSafeInteger(value.revision) || (value.revision as number) < 0 || !Array.isArray(value.revisions)
    || value.revisions.length !== value.revision) return null
  let previous = 0n
  for (const [index, entry] of value.revisions.entries()) {
    if (!object(entry) || !exact(entry, REVISION_FIELDS) || entry.revision !== index + 1 || !text(entry.orderVersionAfter) || !version.test(entry.orderVersionAfter)
      || BigInt(entry.orderVersionAfter) <= previous || BigInt(entry.orderVersionAfter) > BigInt(value.version)
      || !text(entry.previousSupplierPartyId) || !guid.test(entry.previousSupplierPartyId)
      || !text(entry.supplierPartyId) || !guid.test(entry.supplierPartyId) || entry.previousSupplierPartyId === entry.supplierPartyId
      || !text(entry.preparedBy) || !text(entry.previousApprovedBy) || !text(entry.reason)
      || !timestamp(entry.previousApprovedAtUtc) || !timestamp(entry.recordedAtUtc)) return null
    previous = BigInt(entry.orderVersionAfter)
  }
  return value as unknown as SupplierRevisionHistory
}
export async function fetchSupplierRevisions(companyId: string, orderId: string, signal?: AbortSignal): Promise<ViewOutcome<SupplierRevisionHistory>> {
  try {
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/supplier-revisions`, { headers: purchasingHeaders(companyId), signal })
    let body: unknown = null; try { body = await response.json() } catch { /* malformed */ }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const value = decodeSupplierRevisionHistory(body, companyId, orderId)
    return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}

export interface SupplierRevisionPriceRequest extends CreatePurchaseOrderLinePriceRequest {
  readonly priceUnitId?: string
  readonly deferralPercent?: string
  readonly deferralAmount?: string
}
export type SupplierRevisionCommand =
  | { readonly kind: 'supplier'; readonly expectedVersion: string; readonly supplierPartyId: string; readonly companyAccountId: string; readonly reason: string
      /** ALS-D126: the active future allocations the actor saw and confirmed; the revision releases exactly these. */
      readonly seenAllocations?: readonly SupplierRevisionSeenAllocation[] }
  | { readonly kind: 'price'; readonly expectedVersion: string; readonly lineId: string; readonly price: SupplierRevisionPriceRequest }
export interface SupplierRevisionSeenAllocation { readonly allocationId: string; readonly expectedAllocationVersion: string }
/** ALS-D126: an active future allocation refuses the unconfirmed revision; a confirmed set that is no longer the active set too. */
export const SUPPLIER_REVISION_ALLOCATION_CODES = ['purchasing_line_has_allocations', 'purchasing_future_allocation_set_changed'] as const
export interface SupplierRevisionAttempt { readonly actorId: string; readonly companyId: string; readonly orderId: string; readonly idempotencyKey: string; readonly command: SupplierRevisionCommand }
export type SupplierRevisionOutcome = { readonly type: 'applied' } | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown' }
export async function sendSupplierRevision(attempt: SupplierRevisionAttempt): Promise<SupplierRevisionOutcome> {
  const command = attempt.command
  const suffix = command.kind === 'supplier' ? 'supplier-revisions' : `lines/${encodeURIComponent(command.lineId)}/supplier-revision-price`
  const body = command.kind === 'supplier' ? { expectedVersion: command.expectedVersion, supplierPartyId: command.supplierPartyId,
    companyAccountId: command.companyAccountId, reason: command.reason,
    ...(command.seenAllocations === undefined ? {} : { seenAllocations: command.seenAllocations }) }
    : { expectedVersion: command.expectedVersion, price: command.price }
  try {
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(attempt.orderId)}/${suffix}`, {
      method: 'POST', headers: { ...purchasingHeaders(attempt.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': attempt.idempotencyKey },
      body: JSON.stringify(body),
    })
    let answer: unknown = null; try { answer = await response.json() } catch { /* unknown */ }
    if (response.status === 200) {
      const decoded = decodePurchaseOrderCommandResponse(answer)
      if (decoded === null || decoded.detail.order.companyId !== attempt.companyId || decoded.detail.order.id !== attempt.orderId
        || BigInt(decoded.detail.order.version) <= BigInt(command.expectedVersion)
        || (command.kind === 'supplier' && !decoded.isReplay && (decoded.detail.order.supplierPartyId !== command.supplierPartyId || decoded.detail.order.companyAccountId !== command.companyAccountId))
        || (command.kind === 'price' && !decoded.detail.lines.some(line => line.lineId === command.lineId))) return { type: 'unknown' }
      const replay = response.headers.get('Idempotency-Replayed'); const correlation = response.headers.get('Idempotency-Original-Correlation-Id')
      if (decoded.isReplay ? replay !== 'true' || correlation !== decoded.originalCorrelationId : replay !== null || correlation !== null) return { type: 'unknown' }
      return { type: 'applied' }
    }
    return [400, 401, 403, 404, 409, 415, 422].includes(response.status)
      ? { type: 'failed', status: response.status, code: object(answer) && text(answer.code) ? answer.code : null } : { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}

export function supplierRevisionAttemptKey(epoch: number, actor: string, company: string, order: string): string {
  return `goymen.purchasing.supplier-revision.v1:${epoch}:${actor}:${company}:${order}`
}
export function readSupplierRevisionAttempt(key: string, actorId: string, companyId: string, orderId: string): SupplierRevisionAttempt | null {
  const raw = sessionStorage.getItem(key); if (raw === null) return null
  const value: unknown = JSON.parse(raw)
  if (!object(value) || !exact(value, ATTEMPT_FIELDS) || value.actorId !== actorId || value.companyId !== companyId || value.orderId !== orderId
    || !text(value.idempotencyKey) || !object(value.command) || !text(value.command.expectedVersion) || !version.test(value.command.expectedVersion)) throw new Error('invalid pending attempt')
  const command = value.command
  if (command.kind === 'supplier' ? !exact(command, SUPPLIER_COMMAND_FIELDS, ['seenAllocations']) || !text(command.supplierPartyId) || !guid.test(command.supplierPartyId)
      || !text(command.companyAccountId) || !guid.test(command.companyAccountId) || !text(command.reason)
      || !validSeen(command.seenAllocations)
    : command.kind !== 'price' || !exact(command, PRICE_COMMAND_FIELDS) || !text(command.lineId) || !guid.test(command.lineId) || !validPrice(command.price)) throw new Error('invalid pending command')
  return value as unknown as SupplierRevisionAttempt
}
function validSeen(value: unknown): boolean {
  if (value === undefined) return true
  if (!Array.isArray(value)) return false
  const ids = new Set<string>()
  for (const item of value) {
    if (!object(item) || Object.keys(item).length !== 2 || !text(item.allocationId) || !guid.test(item.allocationId)
      || !text(item.expectedAllocationVersion) || !version.test(item.expectedAllocationVersion) || ids.has(item.allocationId)) return false
    ids.add(item.allocationId)
  }
  return true
}
function validPrice(value: unknown): boolean {
  if (!object(value)) return false
  const decimalText = (item: unknown) => text(item) && item.length <= 64 && /^[0-9]+(?:\.[0-9]+)?$/.test(item)
  const decimals = ['unitPrice', 'discountPercent', 'discountAmount', 'deferralPercent', 'deferralAmount']
  return decimalText(value.unitPrice)
    && Object.keys(value).every(key => decimals.includes(key) || key === 'priceUnitId')
    && decimals.every(key => value[key] === undefined || decimalText(value[key]))
    && (value.priceUnitId === undefined || text(value.priceUnitId) && guid.test(value.priceUnitId))
}
export function saveSupplierRevisionAttempt(key: string, attempt: SupplierRevisionAttempt): void {
  sessionStorage.setItem(key, JSON.stringify(attempt))
  if (sessionStorage.getItem(key) !== JSON.stringify(attempt)) throw new Error('pending attempt not retained')
}
export function clearSupplierRevisionAttempt(key: string, attempt: SupplierRevisionAttempt): void {
  const current = readSupplierRevisionAttempt(key, attempt.actorId, attempt.companyId, attempt.orderId)
  if (current?.idempotencyKey === attempt.idempotencyKey) sessionStorage.removeItem(key)
}
