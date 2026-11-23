/**
 * The plan's termin suggestion transport (URT-S10, F-010b): `GET /api/v1/manufacturing/plans/{planId}/due-date-suggestion`.
 *
 * A read under the plan read grant. It answers a `ViewOutcome` like the other reads: an exact 200 whose body is this
 * contract for the plan asked for, or the HTTP status as it came (another company's plan is the same 404 as an unknown
 * id). Nothing is cached by the browser: the suggestion follows the clock, the calendar and the capacity profile.
 */

import { MANUFACTURING_BASE, devHeaders } from './api'
import { decodeDueDateSuggestion, type DueDateSuggestionView } from './dueDateSuggestionContract'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchDueDateSuggestion(
  companyId: string,
  planId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DueDateSuggestionView>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/plans/${encodeURIComponent(planId)}/due-date-suggestion`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
      cache: 'no-store',
      signal,
    })
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
  const suggestion = decodeDueDateSuggestion(body, planId)
  return suggestion === null ? { type: 'malformed' } : { type: 'ok-single', data: suggestion }
}
