/**
 * YOL-D31 (WP14) transport — the Cari card's group-company link, the same thin shape as ./tradeNameApi.ts: a side read that
 * sends the client-contract declaration and the selected company (it does not move the readiness slot), and a versioned PUT
 * through `putVersionedCommand` (one Idempotency-Key per attempt, exact 204, no acknowledgment = unknown outcome).
 * `groupCompanyId` is always sent (the contract requires the member); `null` clears the link.
 */

import { API_BASE, contractRequestHeader } from './clientContract'
import { putVersionedCommand, type CommandContext, type CommandOutcome, type ReplayableFailures } from './commandApi'
import {
  GROUP_COMPANY_LINK_PROBLEM_CODE,
  decodeGroupCompanyLinkView,
  groupCompanyLinkPath,
  groupCompanyLinkProblemCodeOf,
  type GroupCompanyLinkView,
} from './groupCompanyLink'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export type GroupCompanyLinkReadOutcome =
  | { readonly type: 'ok'; readonly data: GroupCompanyLinkView }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

/** The selected company's view of the party's group-company link (a side read of the Cari card). */
export async function fetchGroupCompanyLink(
  partyId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<GroupCompanyLinkReadOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${groupCompanyLinkPath(partyId)}`, {
      headers: { ...devHeaders(), ...contractRequestHeader(), 'X-Company-Id': companyId },
      signal,
    })
  } catch (error) {
    if (signal?.aborted) return { type: 'aborted' }
    if (!navigator.onLine) return { type: 'offline' }
    if (error instanceof TypeError) return { type: 'network-error' }
    throw error
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    body = null
  }

  if (response.status !== 200) {
    return { type: 'http', status: response.status, code: groupCompanyLinkProblemCodeOf(body) }
  }
  const data = decodeGroupCompanyLinkView(body, { partyId, companyId })
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

/** `UpdateCounterpartyGroupCompanyLinkV1` stores 204, 404 (not found) and 409 (version conflict). */
export const GROUP_COMPANY_LINK_REPLAYABLE: ReplayableFailures = new Map([
  [404, new Set<string>([GROUP_COMPANY_LINK_PROBLEM_CODE.notFound])],
  [409, new Set<string>([GROUP_COMPANY_LINK_PROBLEM_CODE.versionConflict])],
])

/** Set or clear (`null`) the link; `expectedVersion` is the PARTY version of the read the form opened on. */
export function updateGroupCompanyLink(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  groupCompanyId: string | null,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    groupCompanyLinkPath(partyId),
    context,
    expectedVersion,
    { groupCompanyId },
    signal,
    GROUP_COMPANY_LINK_REPLAYABLE,
  )
}
