/**
 * UI-65 "Kontrol Merkezi" — the read-only wire contract of API-37 (`GET /api/v1/sales/control-center`), pinned to the backend
 * `SalesControlCenterContractsV1.cs` and to `SalesControlCenterResponse` in `contracts/openapi/goymen-erp-v1.json`
 * (./controlCenterContract.test.ts).
 *
 * The Sales strict rules apply (./dispatchContract.ts): every member PRESENT (nullable ones as explicit null), no unknown member,
 * integers are JSON numbers (strict number handling), enums by explicit value. A decoder returns null for anything it cannot vouch
 * for; the caller fails closed.
 *
 * The screen decides NOTHING: which record is listed, its severity, its age in days, the grace days (SAT-D95) and the tolerance
 * class of an official-document difference (SAT-D229) are the server's. There is no amount on this wire.
 *
 * SAT-D246 (EK49): an entry names its orders as the `salesOrderIds` list (never a single `salesOrderId`) — exactly one for a
 * shipment entry, one or more for an invoice entry (an invoice may span several orders), each order once, in the server's order.
 */

import { isDateOnly, isReferenceId } from './wirePrimitives'

export const SALES_CONTROL_CENTER_PATH = '/sales/control-center'

export const CONTROL_CENTER_KINDS = [
  'UninvoicedDispatchOverdue',
  'InvoiceNeedsReview',
  'InvoiceAwaitingOfficialMatch',
  'OfficialDocumentDeviation',
  'InvoiceFinalizationBlocked',
  'InvoiceFinanceAdmissionWaiting',
] as const
export type ControlCenterKind = (typeof CONTROL_CENTER_KINDS)[number]

export const CONTROL_CENTER_SEVERITIES = ['Critical', 'ApprovalRequired', 'WithinTolerance', 'Info'] as const
export type ControlCenterSeverity = (typeof CONTROL_CENTER_SEVERITIES)[number]

export const CONTROL_CENTER_CHECK_STATES = ['Measured', 'NoSource', 'Unavailable'] as const
export type ControlCenterCheckState = (typeof CONTROL_CENTER_CHECK_STATES)[number]

export interface ControlCenterCheck {
  readonly kind: ControlCenterKind
  readonly state: ControlCenterCheckState
  /** A technical read bound cut the check's source: its list may be incomplete. */
  readonly truncated: boolean
}

export interface ControlCenterItem {
  readonly kind: ControlCenterKind
  readonly severity: ControlCenterSeverity
  readonly partyId: string
  readonly companyAccountId: string
  /** The dispatch note's system number or the invoice's document number. */
  readonly reference: string
  /** The shipment day or the invoice's business day (Europe/Istanbul). */
  readonly referenceDate: string
  /** Whole days from `referenceDate` to the business date, as the server counted them. */
  readonly ageDays: number
  /** The number of lines the entry concerns (1 for a shipment). */
  readonly count: number
  readonly invoiceId: string | null
  readonly dispatchNoteId: string | null
  readonly shipmentId: string | null
  /** Every order the entry concerns, each once, in the server's order (SAT-D246): one for a shipment, one or more for an invoice. */
  readonly salesOrderIds: readonly string[]
}

export interface ControlCenter {
  readonly businessDate: string
  readonly uninvoicedDispatchGraceDays: number
  /** One state per kind, in the server's order. */
  readonly checks: readonly ControlCenterCheck[]
  /** Most severe, then oldest, first — the server's order, never re-sorted here. */
  readonly items: readonly ControlCenterItem[]
}

type Wire = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Wire | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Wire
  if (Object.keys(obj).length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const TEXT_MAX = 512
const isText = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= TEXT_MAX
const isCount = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0
const isPositive = (v: unknown): v is number => isCount(v) && v >= 1
const isNullableId = (v: unknown): v is string | null => v === null || isReferenceId(v)
const ORDER_IDS_MAX = 512
/** A non-empty list of distinct order identities (compared case-insensitively). */
const isOrderIds = (v: unknown): v is readonly string[] => {
  if (!Array.isArray(v) || v.length === 0 || v.length > ORDER_IDS_MAX || !v.every(isReferenceId)) return false
  return new Set(v.map((id: string) => id.toLowerCase())).size === v.length
}
const oneOf = <T extends string>(values: readonly T[]) => (v: unknown): v is T => typeof v === 'string' && (values as readonly string[]).includes(v)
const isKind = oneOf(CONTROL_CENTER_KINDS)
const isSeverity = oneOf(CONTROL_CENTER_SEVERITIES)
const isCheckState = oneOf(CONTROL_CENTER_CHECK_STATES)

export const isControlCenterKind = isKind

const CHECK_KEYS = ['kind', 'state', 'truncated'] as const

function decodeCheck(value: unknown): ControlCenterCheck | null {
  const o = exact(value, CHECK_KEYS)
  if (o === null || !isKind(o.kind) || !isCheckState(o.state) || typeof o.truncated !== 'boolean') return null
  return { kind: o.kind, state: o.state, truncated: o.truncated }
}

const ITEM_KEYS = [
  'kind', 'severity', 'partyId', 'companyAccountId', 'reference', 'referenceDate', 'ageDays', 'count', 'invoiceId',
  'dispatchNoteId', 'shipmentId', 'salesOrderIds',
] as const

function decodeItem(value: unknown): ControlCenterItem | null {
  const o = exact(value, ITEM_KEYS)
  if (
    o === null || !isKind(o.kind) || !isSeverity(o.severity) || !isReferenceId(o.partyId) || !isReferenceId(o.companyAccountId)
    || !isText(o.reference) || !isDateOnly(o.referenceDate) || !isCount(o.ageDays) || !isPositive(o.count)
    || !isNullableId(o.invoiceId) || !isNullableId(o.dispatchNoteId) || !isNullableId(o.shipmentId) || !isOrderIds(o.salesOrderIds)
  ) return null
  // The identities agree with the kind: a shipment entry names its dispatch note, shipment and its one order and no invoice
  // (one line); an invoice entry names its invoice and its orders, and no dispatch note or shipment.
  if (o.kind === 'UninvoicedDispatchOverdue') {
    if (o.invoiceId !== null || o.dispatchNoteId === null || o.shipmentId === null || o.salesOrderIds.length !== 1 || o.count !== 1) return null
  } else if (o.invoiceId === null || o.dispatchNoteId !== null || o.shipmentId !== null) {
    return null
  }
  return {
    kind: o.kind,
    severity: o.severity,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    reference: o.reference,
    referenceDate: o.referenceDate,
    ageDays: o.ageDays,
    count: o.count,
    invoiceId: o.invoiceId,
    dispatchNoteId: o.dispatchNoteId,
    shipmentId: o.shipmentId,
    salesOrderIds: [...o.salesOrderIds],
  }
}

const RESPONSE_KEYS = ['businessDate', 'uninvoicedDispatchGraceDays', 'checks', 'items'] as const

export function decodeControlCenter(value: unknown): ControlCenter | null {
  const o = exact(value, RESPONSE_KEYS)
  if (o === null || !isDateOnly(o.businessDate) || !isPositive(o.uninvoicedDispatchGraceDays)
    || !Array.isArray(o.checks) || !Array.isArray(o.items)) return null
  const checks: ControlCenterCheck[] = []
  for (const raw of o.checks) {
    const check = decodeCheck(raw)
    if (check === null) return null
    checks.push(check)
  }
  // Every check states itself exactly once: a missing check would read as a silent "nothing".
  if (checks.length !== CONTROL_CENTER_KINDS.length || CONTROL_CENTER_KINDS.some((kind) => checks.filter((c) => c.kind === kind).length !== 1)) {
    return null
  }
  const items: ControlCenterItem[] = []
  for (const raw of o.items) {
    const item = decodeItem(raw)
    if (item === null) return null
    // An unavailable check claims nothing; an entry under it is not to be believed. No entry is dated after today.
    if (checks.find((c) => c.kind === item.kind)?.state === 'Unavailable' || item.referenceDate > o.businessDate) return null
    items.push(item)
  }
  return { businessDate: o.businessDate, uninvoicedDispatchGraceDays: o.uninvoicedDispatchGraceDays, checks, items }
}
