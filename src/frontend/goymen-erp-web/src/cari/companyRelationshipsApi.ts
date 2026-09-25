/**
 * Transport for the C-03 company relationships panel: the GROUP form of
 * `GET /api/v1/counterparties/{partyId}` (`GetCounterpartyV1`), sent WITHOUT `X-Company-Id`.
 *
 * Two deliberate differences from `fetchCounterpartyDetail`:
 *
 * 1. No company header. The server resolves every company the actor may read in and
 *    returns one account per company. `companyId` is never sent as a query parameter
 *    either: the server refuses it (400), and the scope is the server's to decide.
 *
 * 2. It does NOT write the client contract readiness slot. That slot decides whether
 *    C-03's commands may open, and it is fed only by the company-bound read of the same
 *    context. A group read succeeding (or failing) says nothing about that context's
 *    command compatibility, so letting it write there would open or close the status
 *    action on evidence that is not about it. The declaration header is still SENT —
 *    the server needs it to return a role-less account at all.
 */

import { decodeCompanyRelationships, type CompanyRelationships } from './companyRelationshipsContract'
import { API_BASE, contractRequestHeader } from './clientContract'
import type { ViewOutcome } from '../shell/uiState'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export async function fetchCompanyRelationships(
  partyId: string,
  activeCompanyId: string,
  knownCompanyIds: ReadonlySet<string>,
  signal?: AbortSignal,
): Promise<ViewOutcome<CompanyRelationships>> {
  const headers: Record<string, string> = DEV_IDENTITY_ENABLED
    ? { 'X-Development-User': DEV_USER, ...contractRequestHeader() }
    : { ...contractRequestHeader() }

  let response: Response
  try {
    response = await fetch(`${API_BASE}/counterparties/${encodeURIComponent(partyId)}`, {
      headers,
      signal,
    })
  } catch (error) {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    if (error instanceof TypeError) return { type: 'network-error' }
    throw error
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    // Cut off by our own cancellation is ours, not the server's (see detailApi.ts).
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }

  // Exact 200 only. The whole 2xx range is not success.
  if (response.status !== 200) return { type: 'http', status: response.status, body }

  const relationships = decodeCompanyRelationships(body, partyId, activeCompanyId, knownCompanyIds)
  return relationships === null ? { type: 'malformed' } : { type: 'ok-single', data: relationships }
}
