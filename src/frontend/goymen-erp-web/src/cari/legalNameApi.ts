/**
 * F-027 / ALS-D95 transport — the Cari card's official legal name.
 *
 * Same thin hand-written shape as ./tradeNameApi.ts, consuming the generated contract (see ./legalName.ts):
 *  - The READ is a side read of the Cari card: it sends the client-contract declaration and the selected company, and
 *    deliberately does NOT move the readiness slot (the card's own detail read owns it).
 *  - The COMMAND shares the write gate, one Idempotency-Key per attempt, the exact-204 rule and the "no acknowledgment =
 *    unknown outcome" rule through `putVersionedCommand`, so the PARTY version travels as lossless decimal text.
 */

import { API_BASE, contractRequestHeader } from './clientContract'
import { putVersionedCommand, type CommandContext, type CommandOutcome, type ReplayableFailures } from './commandApi'
import {
  LEGAL_NAME_PROBLEM_CODE,
  decodeLegalNameView,
  legalNamePath,
  legalNameProblemCodeOf,
  type LegalNameView,
} from './legalName'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export type LegalNameReadOutcome =
  | { readonly type: 'ok'; readonly data: LegalNameView }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

/** The selected company's view of the party's legal name (a side read of the Cari card). */
export async function fetchLegalName(
  partyId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<LegalNameReadOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${legalNamePath(partyId)}`, {
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
    return { type: 'http', status: response.status, code: legalNameProblemCodeOf(body) }
  }
  const data = decodeLegalNameView(body, { partyId, companyId })
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

/** `UpdateCounterpartyLegalNameV1` stores 204, 404 (not found) and 409 (version conflict). */
export const LEGAL_NAME_REPLAYABLE: ReplayableFailures = new Map([
  [404, new Set<string>([LEGAL_NAME_PROBLEM_CODE.notFound])],
  [409, new Set<string>([LEGAL_NAME_PROBLEM_CODE.versionConflict])],
])

/** Change the legal name; `expectedVersion` is the PARTY version of the read the form opened on. */
export function updateLegalName(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  legalName: string,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    legalNamePath(partyId),
    context,
    expectedVersion,
    { legalName },
    signal,
    LEGAL_NAME_REPLAYABLE,
  )
}
