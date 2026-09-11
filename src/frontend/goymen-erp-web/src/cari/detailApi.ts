/**
 * C-03 Cari 360 detail transport.
 *
 * Mirrors `GetCounterpartyV1` — `GET /api/v1/counterparties/{partyId}` returning
 * `CounterpartyDetailView` (Codex #170). The runtime decoding in
 * ./detailContract.ts is what survives the generated-client migration.
 */

import { decodeCounterpartyDetail, type CounterpartyDetail } from './detailContract'
import {
  API_BASE,
  contractAckOf,
  contractRequestHeader,
  recordContractRead,
  type ContractContext,
} from './clientContract'
import type { ViewOutcome } from '../shell/uiState'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

/**
 * Fetch one counterparty, bound to the active company.
 *
 * `partyId` comes from the URL, so it is attacker-controlled in the ordinary
 * sense: a user can type any id. That is fine and deliberate — the server
 * re-authorizes every request (ERP-000K §10.2) and answers a non-disclosing 404
 * for a record that does not exist, is foreign, or is hidden by policy. The
 * client's job is to send it unchanged and never to guess on its behalf.
 */
export async function fetchCounterpartyDetail(
  partyId: string,
  activeCompanyId: string,
  signal?: AbortSignal,
  contractContext?: ContractContext | null,
): Promise<ViewOutcome<CounterpartyDetail>> {
  const headers: Record<string, string> = DEV_IDENTITY_ENABLED
    ? { 'X-Development-User': DEV_USER, ...contractRequestHeader() }
    : { ...contractRequestHeader() }
  headers['X-Company-Id'] = activeCompanyId

  let response: Response
  try {
    response = await fetch(`${API_BASE}/counterparties/${encodeURIComponent(partyId)}`, {
      headers,
      signal,
    })
  } catch (error) {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    // A request that never produced a response is a transport failure, not a
    // malformed body — the shell distinguishes these (PR #155).
    if (error instanceof TypeError) return { type: 'network-error' }
    throw error
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    body = null
  }

  const ack = contractAckOf(response)

  // Exact 200 only. The whole 2xx range is not success.
  if (response.status !== 200) {
    recordContractRead(contractContext ?? null, {
      status: response.status,
      bodyValidated: false,
      ack,
    })
    return { type: 'http', status: response.status, body }
  }

  const detail = decodeCounterpartyDetail(body, activeCompanyId)
  recordContractRead(contractContext ?? null, {
    status: response.status,
    bodyValidated: detail !== null,
    ack,
  })
  if (detail === null) return { type: 'malformed' }
  return { type: 'ok-single', data: detail }
}
