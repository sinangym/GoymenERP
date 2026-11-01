/**
 * API-04: the three HTTP calls of the S-04 header revision — the revisable header read, the sales-owner list (SAT-D27) and the
 * keyed revision command. The rules are the other Sales calls': a success is only a decoded answer of THIS draft and company;
 * a revision's success is the draft after the change, with the replay header and `isReplay` agreeing.
 */

import { headers, readFailure, request, type CommandContext } from './api'
import { decodeDraftMutation, decodeProblem, STATE_COMMAND_STATUS, type DraftMutation } from './contract'
import {
  decodeDraftHeader,
  decodeSalesOwnerOptions,
  SALES_REFERENCE_SALES_OWNERS_PATH,
  salesDraftHeaderPath,
  salesDraftHeaderRevisionPath,
  type DraftHeader,
  type HeaderRevisionRequest,
  type SalesOwnerOptions,
} from './headerRevision'
import type { OrderCommandOutcome } from './orderAttempt'
import type { ViewOutcome } from '../shell/uiState'

/** The picker's page size (`SalesOwnerPickerLimitsV1.DefaultLimit`): a technical bound, not a policy. */
export const SALES_OWNER_PAGE = 25

export async function fetchDraftHeader(draftId: string, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<DraftHeader>> {
  const raw = await request(salesDraftHeaderPath(draftId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const header = decodeDraftHeader(raw.body)
  return header === null ? { type: 'malformed' } : { type: 'ok-single', data: header }
}

export type SalesOwnersOutcome =
  | { readonly type: 'ready'; readonly data: SalesOwnerOptions }
  | { readonly type: 'http'; readonly status: number }
  | { readonly type: 'network' }
  | { readonly type: 'malformed' }

/** SAT-D27: the company's people who may be the sales owner, narrowed by the server's own `q` (never filtered here). */
export async function fetchSalesOwners(companyId: string, q: string, signal?: AbortSignal): Promise<SalesOwnersOutcome> {
  const params = new URLSearchParams({ limit: String(SALES_OWNER_PAGE) })
  if (q.trim() !== '') params.set('q', q.trim())
  const raw = await request(`${SALES_REFERENCE_SALES_OWNERS_PATH}?${params.toString()}`, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return { type: 'network' }
  if (raw.status !== 200) return { type: 'http', status: raw.status }
  const data = decodeSalesOwnerOptions(raw.body, companyId)
  return data === null ? { type: 'malformed' } : { type: 'ready', data }
}

export async function sendDraftHeaderRevision(
  payload: HeaderRevisionRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(
    salesDraftHeaderRevisionPath(context.draftId),
    {
      method: 'POST',
      headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (mutation === null || mutation.isReplay !== raw.replayed || mutation.detail.draft.id !== context.draftId
    || mutation.detail.draft.companyId !== context.companyId) return { type: 'malformed' }
  return { type: 'succeeded', data: mutation }
}
