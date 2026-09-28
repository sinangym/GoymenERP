/**
 * Logistics loading-instruction transport, pinned to LOG-4a (#350). The Sales lane's shape:
 *
 *   POST /api/v1/logistics/loading-instructions            → 201 LoadingInstructionResponse (isReplay=false)
 *                                                          → 201 same body, isReplay=true + `Idempotency-Replayed`
 *   GET  …/loading-instructions?pageSize&cursor&status     → LoadingInstructionListResponse
 *   GET  …/loading-instructions/{id}                       → LoadingInstructionDetailView
 *   POST …/loading-instructions/{id}/issue                 → 200 (Draft → Issued); replay 200 + header
 *   POST …/loading-instructions/{id}/lines/{lineId}/loads → 200 (a loaded share); replay 200 + header
 *   POST …/loading-instructions/{id}/weighings             → 200 (AT-053 weighbridge record); replay 200 + header
 *
 * The transport invents no field and carries no business rule. It adds the consumer checks the
 * contract asks for: a command answer is success only when status and `isReplay` agree with the
 * header AND the body describes THIS attempt under THIS company (a create: every line by value,
 * the quantity TEXT included); a read is data only when every record belongs to the company it
 * was read under. Anything else is `malformed` — the record may exist, so the caller keeps the key.
 *
 * The line picker's read is the price-free Logistics approved-order-lines read under the create grant
 * (Sinan §4: the Sevkiyatçı sees orders, never prices); no Sales grant is needed.
 *
 * `X-Company-Id` is the committed shell company — request context, never a body member.
 */

import {
  APPROVED_ORDER_LINES_PATH,
  CREATED_STATUS,
  LOGISTICS_PATH,
  MUTATION_STATUS,
  REPLAY_HEADER,
  decodeApprovedOrderLinePage,
  decodeInstructionDetail,
  decodeInstructionMutation,
  decodeInstructionPage,
  decodeProblem,
  instructionDetailPath,
  issuePath,
  loadsPath,
  weighingsPath,
  type ApprovedOrderLinePage,
  type CreateInstructionRequest,
  type InstructionDetail,
  type InstructionMutation,
  type InstructionPage,
  type InstructionStatus,
  type IssueRequest,
  type RecordLoadRequest,
  type RecordWeighingRequest,
} from './contract'
import type { CommandOutcome } from './instructionAttempt'
import type { ViewOutcome } from '../shell/uiState'
import { decodeShipmentAllocations, deliveryPath, shipmentPath, SHIPMENT_ALLOCATIONS_PATH,
  type RecordDeliveryRequest, type RecordShipmentRequest, type ShipmentAllocationPage } from './realizationContract'
import type { InstructionLine } from './contract'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

type Raw =
  | { readonly status: number; readonly body: unknown; readonly location: string | null; readonly replayed: boolean }
  | { readonly failure: 'aborted' | 'offline' | 'unreachable' }

async function request(path: string, init: RequestInit, signal: AbortSignal | undefined): Promise<Raw> {
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
    if (signal?.aborted) return { failure: 'aborted' }
    body = null
  }
  return {
    status: response.status,
    body,
    location: response.headers.get('Location'),
    replayed: response.headers.get(REPLAY_HEADER) === 'true',
  }
}

/* ── Reads ────────────────────────────────────────────────────────────────── */

function readFailure<T>(failure: 'aborted' | 'offline' | 'unreachable'): ViewOutcome<T> {
  if (failure === 'aborted') return { type: 'fence-stale' }
  if (failure === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

/** Source bound: 1..100, default 50. */
export const PAGE_SIZE = 50

export async function fetchInstructionPage(
  cursor: string | null,
  companyId: string,
  signal?: AbortSignal,
  status: InstructionStatus | null = null,
): Promise<ViewOutcome<InstructionPage>> {
  const params = new URLSearchParams({ pageSize: String(PAGE_SIZE) })
  if (cursor !== null) params.set('cursor', cursor)
  // An exact status narrows the list on the server; no client-side filtering.
  if (status !== null) params.set('status', status)
  const raw = await request(`${LOGISTICS_PATH}?${params.toString()}`, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const page = decodeInstructionPage(raw.body)
  // A row of another company is a tenant-binding breach, not a row to show.
  if (page === null || page.items.some((item) => item.companyId !== companyId)) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}

export async function fetchInstructionDetail(
  instructionId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<InstructionDetail>> {
  const raw = await request(instructionDetailPath(instructionId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const detail = decodeInstructionDetail(raw.body, instructionId)
  if (detail === null || detail.instruction.companyId !== companyId) return { type: 'malformed' }
  return { type: 'ok-single', data: detail }
}

/* ── The line picker's read: price-free approved order lines ─────────────── */

/**
 * The APPROVED sales lines a loading instruction may be opened on (LOG-4b, under the create grant).
 * Every item must belong to the company it was read under. Never a default: an empty page means no
 * line to pick.
 */
export async function fetchApprovedOrderLines(
  cursor: string | null,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ApprovedOrderLinePage>> {
  const params = new URLSearchParams({ pageSize: String(PAGE_SIZE) })
  if (cursor !== null) params.set('cursor', cursor)
  const raw = await request(`${APPROVED_ORDER_LINES_PATH}?${params.toString()}`, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const page = decodeApprovedOrderLinePage(raw.body)
  if (page === null) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}

/* ── Create ───────────────────────────────────────────────────────────────── */

export interface CreateContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
}

/**
 * Whether a decoded answer describes the attempt that was sent: the company it was sent under,
 * the customer, the business date and every line by value — the source line, the unit and the
 * instructed quantity TEXT: the client sends the canonical text (no leading or trailing zeros) and the
 * server answers the exact value in the same canonical text, so they must be equal. Ids alone prove nothing.
 * The line's product is the Sales owner's answer and is not compared (the client sent none).
 */
export function describesAttempt(mutation: InstructionMutation, sent: CreateInstructionRequest, companyId: string): boolean {
  const { detail } = mutation
  const instruction = detail.instruction
  if (
    instruction.companyId !== companyId ||
    instruction.status !== 'Draft' ||
    instruction.customerPartyId !== sent.customerPartyId ||
    instruction.companyAccountId !== sent.companyAccountId ||
    instruction.effectiveOn !== sent.effectiveOn ||
    detail.lines.length !== sent.lines.length ||
    detail.shares.length !== 0 ||
    detail.weighing !== null
  ) {
    return false
  }
  return sent.lines.every((line) => {
    const answered = detail.lines.find((l) => l.lineId === line.lineId)
    return (
      answered !== undefined &&
      answered.sourceOrderId === line.sourceOrderId &&
      answered.sourceOrderLineId === line.sourceOrderLineId &&
      answered.unitId === line.unitId &&
      answered.instructed === line.quantity &&
      answered.loaded === '0' &&
      !answered.isBlocked
    )
  })
}

function commandFailure(raw: { readonly failure: 'aborted' | 'offline' | 'unreachable' }): CommandOutcome<never> {
  if (raw.failure === 'aborted') return { type: 'aborted' }
  if (raw.failure === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

export async function createInstruction(
  payload: CreateInstructionRequest,
  context: CreateContext,
  signal?: AbortSignal,
): Promise<CommandOutcome<InstructionMutation>> {
  const raw = await request(
    LOGISTICS_PATH,
    {
      method: 'POST',
      headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) return commandFailure(raw)
  if (raw.status !== CREATED_STATUS) {
    // Any other 2xx is outside the contract: the instruction may exist, so the outcome is unknown.
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeInstructionMutation(raw.body)
  if (
    mutation === null ||
    // 201 both times; the replay carries Idempotency-Replayed and says so in the body. A disagreement
    // means this client cannot say which one happened.
    mutation.isReplay !== raw.replayed ||
    !describesAttempt(mutation, payload, context.companyId) ||
    (raw.location !== null && !raw.location.endsWith(`/api/v1${instructionDetailPath(mutation.detail.instruction.id)}`))
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: mutation }
}

/* ── Transitions: issue and record-load ───────────────────────────────────── */

export interface CommandContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
  /** The instruction the command was sent for; an answer about another record is not this command's. */
  readonly instructionId: string
}

async function sendTransition(
  path: string,
  payload: IssueRequest | RecordLoadRequest | RecordShipmentRequest | RecordDeliveryRequest | RecordWeighingRequest,
  context: CommandContext,
  signal: AbortSignal | undefined,
): Promise<CommandOutcome<InstructionMutation>> {
  const raw = await request(
    path,
    {
      method: 'POST',
      headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) return commandFailure(raw)
  if (raw.status !== MUTATION_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeInstructionMutation(raw.body)
  if (
    mutation === null ||
    mutation.isReplay !== raw.replayed ||
    mutation.detail.instruction.id !== context.instructionId ||
    mutation.detail.instruction.companyId !== context.companyId
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: mutation }
}

/** Draft → Issued at the expected version: 200 with the instruction after the transition; the same key again 200 + replay. */
export function issueInstruction(
  payload: IssueRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<CommandOutcome<InstructionMutation>> {
  return sendTransition(issuePath(context.instructionId), payload, context, signal)
}

/** One loaded share on a line: the answer must be THIS instruction, and the share sent must be in it. */
export async function recordLoad(
  lineId: string,
  payload: RecordLoadRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<CommandOutcome<InstructionMutation>> {
  const outcome = await sendTransition(loadsPath(context.instructionId, lineId), payload, context, signal)
  if (outcome.type !== 'succeeded') return outcome
  const share = outcome.data.detail.shares.find((s) => s.shareId === payload.loadId)
  // The recorded share is the one sent, on the line sent, with the quantity TEXT sent.
  if (share === undefined || share.lineId !== lineId || share.quantity !== payload.quantity || share.measurement !== payload.measurement) {
    return { type: 'malformed' }
  }
  return outcome
}

export async function fetchShipmentAllocations(line: InstructionLine, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<ShipmentAllocationPage>> {
  const params = new URLSearchParams({ salesOrderId: line.sourceOrderId, salesOrderLineId: line.sourceOrderLineId, limit: '100' })
  const raw = await request(`${SHIPMENT_ALLOCATIONS_PATH}?${params}`, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const data = decodeShipmentAllocations(raw.body, companyId, line)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}

export async function recordShipment(lineId: string, payload: RecordShipmentRequest, context: CommandContext,
  signal?: AbortSignal): Promise<CommandOutcome<InstructionMutation>> {
  const outcome = await sendTransition(shipmentPath(context.instructionId, lineId), payload, context, signal)
  if (outcome.type !== 'succeeded') return outcome
  const shares = outcome.data.detail.shares.filter((share) => share.shareId === payload.shipmentId)
  const share = shares[0]
  return shares.length !== 1 || share.kind !== 'Shipped' || share.lineId !== lineId || share.quantity !== payload.quantity
    || share.measurement !== null || share.sourceShipmentId !== null || Date.parse(share.recordedAtUtc) !== Date.parse(payload.occurredAtUtc)
    ? { type: 'malformed' } : outcome
}

export async function recordDelivery(lineId: string, payload: RecordDeliveryRequest, context: CommandContext,
  signal?: AbortSignal): Promise<CommandOutcome<InstructionMutation>> {
  const outcome = await sendTransition(deliveryPath(context.instructionId, lineId), payload, context, signal)
  if (outcome.type !== 'succeeded') return outcome
  const shares = outcome.data.detail.shares.filter((share) => share.shareId === payload.deliveryId)
  const share = shares[0]
  return shares.length !== 1 || share.kind !== 'Delivered' || share.lineId !== lineId || share.quantity !== payload.quantity
    || share.measurement !== null || share.sourceShipmentId !== payload.sourceShipmentId
    || Date.parse(share.recordedAtUtc) !== Date.parse(payload.occurredAtUtc) ? { type: 'malformed' } : outcome
}

/**
 * AT-053: the vehicle's manual weighbridge record. The answer must be THIS instruction carrying THE record sent — its
 * id, direction and both weighings as the text sent. Net and difference are the backend's; they are shown, never checked
 * against a client calculation.
 */
export async function recordWeighing(payload: RecordWeighingRequest, context: CommandContext,
  signal?: AbortSignal): Promise<CommandOutcome<InstructionMutation>> {
  const outcome = await sendTransition(weighingsPath(context.instructionId), payload, context, signal)
  if (outcome.type !== 'succeeded') return outcome
  const weighing = outcome.data.detail.weighing
  return weighing === null || weighing.weighingId !== payload.weighingId || weighing.direction !== payload.direction
    || weighing.firstWeighingKg !== payload.firstWeighingKg || weighing.secondWeighingKg !== payload.secondWeighingKg
    ? { type: 'malformed' } : outcome
}
