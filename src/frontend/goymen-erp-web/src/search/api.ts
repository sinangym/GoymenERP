/**
 * AT-002 — the global search transport: GET /api/v1/search?q=<term> under the committed shell company
 * (`X-Company-Id`, request context, never a body member). The transport carries no rule of its own beyond the
 * contract's: 200 is data only when it decodes for exactly this term; anything else is the classified outcome.
 */

import type { ViewOutcome } from '../shell/uiState'
import { SEARCH_PATH, SEARCH_QUERY_PARAM, decodeSearchAnswer, type SearchAnswer } from './searchContract'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export async function fetchGlobalSearch(term: string, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<SearchAnswer>> {
  const params = new URLSearchParams({ [SEARCH_QUERY_PARAM]: term })
  let response: Response
  try {
    response = await fetch(`${API_BASE}${SEARCH_PATH}?${params.toString()}`, {
      headers: {
        ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
        'X-Company-Id': companyId,
      },
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    return { type: 'network-error' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const answer = decodeSearchAnswer(body, term)
  return answer === null ? { type: 'malformed' } : { type: 'ok-single', data: answer }
}
