/**
 * Accounting read transport (ACC-V0): periods, journals and the posting queues.
 *
 * Thin and hand-written, like the other modules' `api.ts`. HANDOFF: when the generated client
 * lands (Issue #122) the fetches move to `src/api/generated/**`; the decoders in ./contract.ts
 * survive that move.
 *
 * Every read carries only `X-Company-Id` (the verified shell company); a success is EXACTLY 200,
 * everything else is the status with its problem body, and nothing is guessed from a partial
 * answer.
 */

import type { ViewOutcome } from '../shell/uiState'
import {
  decodeJournalDetail,
  decodeJournalPage,
  decodePeriodPage,
  decodePostingQueues,
  type JournalDetail,
  type JournalPage,
  type PeriodPage,
  type PostingQueues,
} from './contract'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export const ACCOUNTING_BASE = `${API_BASE}/accounting`

async function read<T>(
  url: string,
  companyId: string,
  decode: (body: unknown) => T | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<T>> {
  let response: Response
  try {
    response = await fetch(url, { headers: { ...devHeaders(), 'X-Company-Id': companyId }, signal })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }
  // EXACT 200: `Response.ok` spans all of 2xx, which is not this contract.
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decode(body)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}

function withQuery(path: string, params: Record<string, string | null>): string {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== null) query.set(key, value)
  }
  const text = query.toString()
  return text === '' ? path : `${path}?${text}`
}

/** `GET /accounting/periods` — the company's periods, newest first. */
export function fetchPeriods(
  companyId: string,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<PeriodPage>> {
  return read(
    withQuery(`${ACCOUNTING_BASE}/periods`, { cursor }),
    companyId,
    (body) => decodePeriodPage(body, companyId),
    signal,
  )
}

/** `GET /accounting/journals` — the journals, optionally of one period (M-04, read-only). */
export function fetchJournals(
  companyId: string,
  periodId: string | null,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<JournalPage>> {
  return read(withQuery(`${ACCOUNTING_BASE}/journals`, { periodId, cursor }), companyId, decodeJournalPage, signal)
}

/** `GET /accounting/journals/{id}` — one journal with its lines. Another company's is a code-less 404. */
export function fetchJournal(
  companyId: string,
  journalId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<JournalDetail>> {
  return read(`${ACCOUNTING_BASE}/journals/${encodeURIComponent(journalId)}`, companyId, decodeJournalDetail, signal)
}

/** `GET /accounting/posting-queues` — the open mapping exceptions and pending valuations (M-01). */
export function fetchPostingQueues(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<PostingQueues>> {
  return read(`${ACCOUNTING_BASE}/posting-queues`, companyId, decodePostingQueues, signal)
}
