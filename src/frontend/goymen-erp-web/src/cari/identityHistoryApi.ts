/**
 * F-027 / KLN-D38 transport — the Cari card's dated identity history and the succession command.
 *
 * Same thin hand-written shape as ./legalNameApi.ts, consuming the generated contract (see ./identityHistory.ts):
 *  - The READ is a side read of the Cari card: it sends the client-contract declaration and the selected company, and
 *    deliberately does NOT move the readiness slot (the card's own detail read owns it).
 *  - The COMMAND shares the write gate, one Idempotency-Key per attempt, the exact-204 rule and the "no acknowledgment =
 *    unknown outcome" rule through `putVersionedCommand`, so the company-ACCOUNT version travels as lossless text.
 */

import { API_BASE, contractRequestHeader } from './clientContract'
import { putVersionedCommand, type CommandContext, type CommandOutcome, type ReplayableFailures } from './commandApi'
import {
  IDENTITY_HISTORY_PROBLEM_CODE,
  decodeIdentityHistoryView,
  identityHistoryPath,
  identityHistoryProblemCodeOf,
  successionPath,
  type IdentityHistoryView,
  type SuccessionPayload,
} from './identityHistory'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export type IdentityHistoryReadOutcome =
  | { readonly type: 'ok'; readonly data: IdentityHistoryView }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

/** The selected company's view of the party's dated identity history (a side read of the Cari card). */
export async function fetchIdentityHistory(
  partyId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<IdentityHistoryReadOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${identityHistoryPath(partyId)}`, {
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
    return { type: 'http', status: response.status, code: identityHistoryProblemCodeOf(body) }
  }
  const data = decodeIdentityHistoryView(body, { partyId, companyId })
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

/**
 * `RecordCounterpartyPartySuccessionV1` stores 204, 404, 409 and 422 for the same key and body. The ALS-D99 422
 * `deactivation_warning_acknowledgement_required` is NOT stored (nothing was decided), so it is never a replay.
 */
export const SUCCESSION_REPLAYABLE: ReplayableFailures = new Map([
  [404, new Set<string>([IDENTITY_HISTORY_PROBLEM_CODE.notFound])],
  [
    409,
    new Set<string>([
      IDENTITY_HISTORY_PROBLEM_CODE.versionConflict,
      IDENTITY_HISTORY_PROBLEM_CODE.successionSuccessorInactive,
      IDENTITY_HISTORY_PROBLEM_CODE.successionCycle,
      IDENTITY_HISTORY_PROBLEM_CODE.successionAlreadyRecorded,
    ]),
  ],
  [
    422,
    new Set<string>([
      IDENTITY_HISTORY_PROBLEM_CODE.successionSameParty,
      IDENTITY_HISTORY_PROBLEM_CODE.successionSameTaxIdentity,
    ]),
  ],
])

/**
 * Record that `payload.successorPartyId` succeeds this cari in the selected company; `expectedVersion` is THIS cari's
 * company-account version of the read the form opened on. It moves no balance.
 */
export function recordPartySuccession(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  payload: SuccessionPayload,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    successionPath(partyId),
    context,
    expectedVersion,
    {
      successorPartyId: payload.successorPartyId,
      kind: payload.kind,
      legalBasis: payload.legalBasis,
      effectiveOn: payload.effectiveOn,
      // ALS-D99: only an acknowledged body carries the flag, so every unacknowledged body is byte-for-byte as before.
      ...(payload.exposureWarningAcknowledged === true ? { exposureWarningAcknowledged: true } : {}),
    },
    signal,
    SUCCESSION_REPLAYABLE,
  )
}
