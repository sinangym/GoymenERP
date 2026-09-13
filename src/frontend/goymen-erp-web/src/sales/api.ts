/**
 * Sales draft transport, pinned to #257 @ 2c1239e. NOT live: no route is mapped.
 *
 *   POST /api/v1/sales/drafts          → 201 SalesOrderDraftResponse (isReplay=false)
 *                                       → 200 same body, isReplay=true (same accepted attempt)
 *   GET  /api/v1/sales/drafts?pageSize&cursor → SalesOrderDraftListResponse
 *   GET  /api/v1/sales/drafts/{id}     → SalesOrderDraftDetailView
 *
 * The transport invents no field and carries no business rule. What it does add is
 * the consumer check the contract asks for — "validate nonempty server IDs, positive
 * version text, company/attempt binding, kind/state/revision and line identities
 * before treating a response as success":
 *  - a create answer is success only when status and `isReplay` agree (201/false,
 *    200/true) AND the body describes THIS attempt: the company it was sent under,
 *    the party, account, currency, dates and notes it carried, and exactly the line
 *    and slice identities it minted. Anything else is `malformed` — the draft may
 *    exist, so the caller keeps the key;
 *  - a list or detail read is data only when every record belongs to the company it
 *    was read under, and a detail only when it is the record that was asked for.
 *
 * `X-Company-Id` is the committed shell company — request context, never a body member.
 */

import {
  CREATED_STATUS,
  REPLAY_STATUS,
  REV_0,
  SALES_DRAFT_PATH,
  decodeDraftDetail,
  decodeDraftMutation,
  decodeDraftPage,
  decodeProblem,
  salesDraftDetailPath,
  type DraftCreateRequest,
  type DraftDetail,
  type DraftMutation,
  type DraftPage,
} from './contract'
import type { OrderCommandOutcome } from './orderAttempt'
import type { ViewOutcome } from '../shell/uiState'

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
  | { readonly status: number; readonly body: unknown; readonly location: string | null }
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
  return { status: response.status, body, location: response.headers.get('Location') }
}

/* ── Reads ────────────────────────────────────────────────────────────────── */

function readFailure<T>(failure: 'aborted' | 'offline' | 'unreachable'): ViewOutcome<T> {
  if (failure === 'aborted') return { type: 'fence-stale' }
  if (failure === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

/** Source bound: 1..100, default 50. */
export const PAGE_SIZE = 50

export async function fetchDraftPage(
  cursor: string | null,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DraftPage>> {
  const params = new URLSearchParams({ pageSize: String(PAGE_SIZE) })
  if (cursor !== null) params.set('cursor', cursor)
  const raw = await request(`${SALES_DRAFT_PATH}?${params.toString()}`, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const page = decodeDraftPage(raw.body)
  // A row of another company is a tenant-binding breach, not a row to show.
  if (page === null || page.items.some((item) => item.companyId !== companyId)) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}

export async function fetchDraftDetail(
  draftId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DraftDetail>> {
  const raw = await request(salesDraftDetailPath(draftId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const detail = decodeDraftDetail(raw.body, draftId)
  if (detail === null || detail.draft.companyId !== companyId) return { type: 'malformed' }
  return { type: 'ok-single', data: detail }
}

/* ── Create ───────────────────────────────────────────────────────────────── */

export interface CreateContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
}

const sameSet = (a: readonly string[], b: readonly string[]) =>
  a.length === b.length && [...a].sort().join('|') === [...b].sort().join('|')

/** Whether a decoded answer describes the attempt that was sent. */
export function describesAttempt(mutation: DraftMutation, sent: DraftCreateRequest, companyId: string): boolean {
  const { detail } = mutation
  const draft = detail.draft
  if (
    draft.companyId !== companyId ||
    draft.revision !== REV_0 ||
    draft.partyId !== sent.partyId ||
    draft.companyAccountId !== sent.companyAccountId ||
    draft.currencyId !== sent.currencyId ||
    draft.businessDate !== sent.businessDate ||
    detail.requestedDeliveryDate !== sent.requestedDeliveryDate ||
    detail.note !== sent.note
  ) {
    return false
  }
  if (!sameSet(detail.lines.map((l) => l.lineId), sent.lines.map((l) => l.lineId))) return false
  for (const line of sent.lines) {
    const answered = detail.lines.find((l) => l.lineId === line.lineId)
    if (
      answered === undefined ||
      answered.productId !== line.productId ||
      answered.orderUnitId !== line.orderUnitId ||
      // Decimal text is retained verbatim by the server, so the text must come back as sent.
      answered.quantity !== line.quantity ||
      answered.requestedDeliveryDate !== line.requestedDeliveryDate ||
      (answered.requestedDeliverySlices === null) !== (line.requestedDeliverySlices === null) ||
      !sameSet(
        (answered.requestedDeliverySlices ?? []).map((s) => s.sliceId),
        (line.requestedDeliverySlices ?? []).map((s) => s.sliceId),
      )
    ) {
      return false
    }
  }
  return true
}

export async function createDraft(
  payload: DraftCreateRequest,
  context: CreateContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(
    SALES_DRAFT_PATH,
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== CREATED_STATUS && raw.status !== REPLAY_STATUS) {
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (
    mutation === null ||
    // 201 is the first commit, 200 the replay of the same accepted attempt. A
    // disagreement means this client cannot say which one happened.
    mutation.isReplay !== (raw.status === REPLAY_STATUS) ||
    !describesAttempt(mutation, payload, context.companyId) ||
    (raw.status === CREATED_STATUS &&
      raw.location !== null &&
      !raw.location.endsWith(`/api/v1${salesDraftDetailPath(mutation.detail.draft.id)}`))
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: mutation }
}
