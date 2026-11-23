/**
 * WP4 (SAT-D475 / SAT-D476) — the Sales approval requests: wire shapes, strict decoders and the transport of
 *  - the quote's conversion requests (`/sales/quotes/{id}/expired-conversion-requests`): open (quote write) and reject
 *    (expired-conversion approval); the quote's own expired-conversion approval answers a request (status `Approved`);
 *  - the order's over-delivery requests (`/sales/drafts/{id}/over-delivery-requests`): opened by the Logistics shipment gate,
 *    approved or rejected here under the over-delivery approval (never by the shipper — the server decides).
 * The decoders are exact (no unknown member, every member typed); anything else is `null` and the read is malformed. The screen
 * judges no band, tolerance or eligibility: the quantities are the server's exact texts, the status is the server's answer.
 */

import { headers, readFailure, request, type Raw } from './api'
import { SALES_DRAFT_PATH, decodeProblem } from './contract'
import type { OrderCommandOutcome } from './orderAttempt'
import { SALES_QUOTE_PATH } from './poolContract'
import type { ViewOutcome } from '../shell/uiState'

export const APPROVAL_REQUEST_KIND = { quote: 'QuoteExpiredConversion', overDelivery: 'OverDelivery' } as const
export type ApprovalRequestKind = (typeof APPROVAL_REQUEST_KIND)[keyof typeof APPROVAL_REQUEST_KIND]

export const APPROVAL_REQUEST_STATUS = { pending: 'Pending', approved: 'Approved', rejected: 'Rejected', stale: 'Stale' } as const
export type ApprovalRequestStatus = (typeof APPROVAL_REQUEST_STATUS)[keyof typeof APPROVAL_REQUEST_STATUS]

export interface ApprovalRequest {
  readonly kind: ApprovalRequestKind
  readonly subjectId: string
  readonly requestNo: number
  readonly subjectRevision: string
  readonly status: ApprovalRequestStatus
  readonly version: string
  readonly lineId: string | null
  readonly ordered: string | null
  readonly quantity: string | null
  readonly requestedTotal: string | null
  readonly loadingInstructionId: string | null
  readonly shipmentId: string | null
  readonly note: string | null
  readonly requestedBy: string
  readonly requestedAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionReason: string | null
}

export interface QuoteConversionRequestBody {
  readonly expectedVersion: string
  readonly note: string | null
}

export interface ApprovalDecisionBody {
  readonly expectedVersion: string
  readonly reason?: string
}

export const APPROVAL_REQUEST_CREATED_STATUS = 201
export const APPROVAL_REQUEST_COMMAND_STATUS = 200

export const quoteConversionRequestsPath = (quoteId: string) =>
  `${SALES_QUOTE_PATH}/${encodeURIComponent(quoteId)}/expired-conversion-requests`
export const quoteConversionRejectionPath = (quoteId: string, requestNo: number) =>
  `${quoteConversionRequestsPath(quoteId)}/${requestNo}/rejection`
export const overDeliveryRequestsPath = (draftId: string) =>
  `${SALES_DRAFT_PATH}/${encodeURIComponent(draftId)}/over-delivery-requests`
export const overDeliveryDecisionPath = (draftId: string, requestNo: number, decision: 'approval' | 'rejection') =>
  `${overDeliveryRequestsPath(draftId)}/${requestNo}/${decision}`

const KEYS = [
  'kind', 'subjectId', 'requestNo', 'subjectRevision', 'status', 'version', 'lineId', 'ordered', 'quantity', 'requestedTotal',
  'loadingInstructionId', 'shipmentId', 'note', 'requestedBy', 'requestedAtUtc', 'decidedBy', 'decidedAtUtc', 'decisionReason',
] as const

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const DECIMAL = /^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/
const COUNTER = /^(?:0|[1-9][0-9]*)$/
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)
const exactKeys = (value: Record<string, unknown>, keys: readonly string[]) =>
  Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key))
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const optional = <T>(value: unknown, check: (candidate: unknown) => candidate is T): value is T | null =>
  value === null || check(value)
const guid = (value: unknown): value is string => typeof value === 'string' && GUID.test(value)
const decimal = (value: unknown): value is string => typeof value === 'string' && DECIMAL.test(value)
const instant = (value: unknown): value is string => typeof value === 'string' && INSTANT.test(value)
const counter = (value: unknown): value is string => typeof value === 'string' && COUNTER.test(value)

/** One request, exactly as the server describes it; any other shape is null. */
export function decodeApprovalRequest(body: unknown): ApprovalRequest | null {
  if (!isRecord(body) || !exactKeys(body, KEYS)) return null
  const kind = body.kind
  const status = body.status
  if (kind !== APPROVAL_REQUEST_KIND.quote && kind !== APPROVAL_REQUEST_KIND.overDelivery) return null
  if (!Object.values(APPROVAL_REQUEST_STATUS).includes(status as ApprovalRequestStatus)) return null
  if (!guid(body.subjectId) || typeof body.requestNo !== 'number' || !Number.isInteger(body.requestNo) || body.requestNo < 1) return null
  if (!counter(body.subjectRevision) || !counter(body.version) || body.version === '0') return null
  if (!text(body.requestedBy) || !instant(body.requestedAtUtc)) return null
  if (!optional(body.lineId, guid) || !optional(body.ordered, decimal) || !optional(body.quantity, decimal)
    || !optional(body.requestedTotal, decimal) || !optional(body.loadingInstructionId, guid) || !optional(body.shipmentId, guid)
    || !optional(body.note, text) || !optional(body.decidedBy, text) || !optional(body.decidedAtUtc, instant)
    || !optional(body.decisionReason, text)) {
    return null
  }
  const overDelivery = kind === APPROVAL_REQUEST_KIND.overDelivery
  const factsFit = overDelivery
    ? body.lineId !== null && body.ordered !== null && body.quantity !== null && body.requestedTotal !== null
      && body.loadingInstructionId !== null && body.shipmentId !== null && body.note === null
    : body.lineId === null && body.ordered === null && body.quantity === null && body.requestedTotal === null
      && body.loadingInstructionId === null && body.shipmentId === null
  const decided = body.decidedBy !== null
  const decisionFits = (body.decidedAtUtc !== null) === decided
    && (status === APPROVAL_REQUEST_STATUS.rejected ? decided && body.decisionReason !== null : body.decisionReason === null)
    && (status === APPROVAL_REQUEST_STATUS.pending || status === APPROVAL_REQUEST_STATUS.stale ? !decided : true)
  if (!factsFit || !decisionFits) return null
  return {
    kind,
    subjectId: body.subjectId,
    requestNo: body.requestNo,
    subjectRevision: body.subjectRevision,
    status: status as ApprovalRequestStatus,
    version: body.version,
    lineId: body.lineId,
    ordered: body.ordered,
    quantity: body.quantity,
    requestedTotal: body.requestedTotal,
    loadingInstructionId: body.loadingInstructionId,
    shipmentId: body.shipmentId,
    note: body.note,
    requestedBy: body.requestedBy,
    requestedAtUtc: body.requestedAtUtc,
    decidedBy: body.decidedBy,
    decidedAtUtc: body.decidedAtUtc,
    decisionReason: body.decisionReason,
  }
}

/** `{items: [...]}` of one subject and one kind, newest first; one foreign or broken item makes the whole read malformed. */
export function decodeApprovalRequestList(body: unknown, kind: ApprovalRequestKind, subjectId: string): readonly ApprovalRequest[] | null {
  if (!isRecord(body) || !exactKeys(body, ['items']) || !Array.isArray(body.items)) return null
  const items: ApprovalRequest[] = []
  for (const raw of body.items) {
    const item = decodeApprovalRequest(raw)
    if (item === null || item.kind !== kind || item.subjectId !== subjectId) return null
    items.push(item)
  }
  for (let index = 1; index < items.length; index++) {
    if (items[index].requestNo >= items[index - 1].requestNo) return null
  }
  return items
}

async function readList(path: string, kind: ApprovalRequestKind, subjectId: string, companyId: string,
  signal?: AbortSignal): Promise<ViewOutcome<readonly ApprovalRequest[]>> {
  const raw = await request(path, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const data = decodeApprovalRequestList(raw.body, kind, subjectId)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}

export const fetchQuoteConversionRequests = (quoteId: string, companyId: string, signal?: AbortSignal) =>
  readList(quoteConversionRequestsPath(quoteId), APPROVAL_REQUEST_KIND.quote, quoteId, companyId, signal)

export const fetchOverDeliveryRequests = (draftId: string, companyId: string, signal?: AbortSignal) =>
  readList(overDeliveryRequestsPath(draftId), APPROVAL_REQUEST_KIND.overDelivery, draftId, companyId, signal)

/** The command answer: the request, whether the server replayed it, bound to the subject the attempt named. */
export interface ApprovalRequestMutation {
  readonly request: ApprovalRequest
  readonly isReplay: boolean
}

export interface ApprovalCommandContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt (request context, never a body member). */
  readonly companyId: string
}

function commandOutcome(raw: Raw, expected: number, accept: (body: unknown) => ApprovalRequest | null): OrderCommandOutcome<ApprovalRequestMutation> {
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== expected) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const item = accept(raw.body)
  return item === null ? { type: 'malformed' } : { type: 'succeeded', data: { request: item, isReplay: raw.replayed } }
}

async function sendCommand(path: string, payload: unknown, expected: number, context: ApprovalCommandContext,
  accept: (item: ApprovalRequest) => boolean, signal?: AbortSignal): Promise<OrderCommandOutcome<ApprovalRequestMutation>> {
  const raw = await request(path, {
    method: 'POST',
    headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
    body: JSON.stringify(payload),
  }, signal)
  return commandOutcome(raw, expected, (body) => {
    const item = decodeApprovalRequest(body)
    return item !== null && accept(item) ? item : null
  })
}

/** SAT-D476: the salesperson asks for the conversion of the expired quote's current revision (201 with the pending request). */
export const requestQuoteConversion = (quoteId: string, payload: QuoteConversionRequestBody, context: ApprovalCommandContext,
  signal?: AbortSignal) =>
  sendCommand(quoteConversionRequestsPath(quoteId), payload, APPROVAL_REQUEST_CREATED_STATUS, context,
    (item) => item.kind === APPROVAL_REQUEST_KIND.quote && item.subjectId === quoteId, signal)

export const rejectQuoteConversionRequest = (quoteId: string, requestNo: number, payload: ApprovalDecisionBody,
  context: ApprovalCommandContext, signal?: AbortSignal) =>
  sendCommand(quoteConversionRejectionPath(quoteId, requestNo), payload, APPROVAL_REQUEST_COMMAND_STATUS, context,
    (item) => item.kind === APPROVAL_REQUEST_KIND.quote && item.subjectId === quoteId && item.requestNo === requestNo, signal)

/** SAT-D475: approve or reject one over-delivery request of the order (the shipper is refused by the server). */
export const decideOverDeliveryRequest = (draftId: string, requestNo: number, decision: 'approval' | 'rejection',
  payload: ApprovalDecisionBody, context: ApprovalCommandContext, signal?: AbortSignal) =>
  sendCommand(overDeliveryDecisionPath(draftId, requestNo, decision), payload, APPROVAL_REQUEST_COMMAND_STATUS, context,
    (item) => item.kind === APPROVAL_REQUEST_KIND.overDelivery && item.subjectId === draftId && item.requestNo === requestNo, signal)
