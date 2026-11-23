/**
 * F-027 / EK-18 #76-#77 transport — duplicate Cari merge.
 *
 * Same thin hand-written shape as ./legalNameApi.ts, consuming the generated contract (see ./merge.ts):
 *  - The READ is a side read of the Cari card: it sends the client-contract declaration and the selected company, and
 *    deliberately does NOT move the readiness slot (the card's own detail read owns it).
 *  - The COMMAND shares the write gate, one Idempotency-Key per attempt, the exact-204 rule and the "no acknowledgment =
 *    unknown outcome" rule through `putVersionedCommand`. The survivor's PARTY version is the fence that function owns;
 *    the duplicate's version travels beside it as lossless decimal text and is refused here, before a request exists,
 *    when it is not one.
 */

import { API_BASE, contractRequestHeader } from './clientContract'
import { putVersionedCommand, type CommandContext, type CommandOutcome, type ReplayableFailures } from './commandApi'
import { MERGE_PROBLEM_CODE, decodeMergeView, mergePath, mergeProblemCodeOf, type MergeView } from './merge'
import { isLosslessVersion } from './wireGuards'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export type MergeReadOutcome =
  | { readonly type: 'ok'; readonly data: MergeView }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

/** The selected company's merge view of the party (a side read of the Cari card). */
export async function fetchMerge(partyId: string, companyId: string, signal?: AbortSignal): Promise<MergeReadOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${mergePath(partyId)}`, {
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
    return { type: 'http', status: response.status, code: mergeProblemCodeOf(body) }
  }
  const data = decodeMergeView(body, { partyId, companyId })
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

/**
 * `ApproveCounterpartyPartyMergeV1` stores 204, 404 (not found) and 409 (version conflict, already merged, target
 * merged, tax identity mismatch).
 */
export const MERGE_REPLAYABLE: ReplayableFailures = new Map([
  [404, new Set<string>([MERGE_PROBLEM_CODE.notFound])],
  [
    409,
    new Set<string>([
      MERGE_PROBLEM_CODE.versionConflict,
      MERGE_PROBLEM_CODE.alreadyMerged,
      MERGE_PROBLEM_CODE.targetMerged,
      MERGE_PROBLEM_CODE.taxIdentityMismatch,
    ]),
  ],
])

export interface MergeApproval {
  readonly mergedPartyId: string
  readonly mergedPartyExpectedVersion: string
  readonly reason: string
}

/** Approve merging the duplicate into the survivor; `expectedVersion` is the survivor's PARTY version of the read. */
export function approveMerge(
  context: CommandContext,
  survivorPartyId: string,
  expectedVersion: string,
  approval: MergeApproval,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  if (!isLosslessVersion(approval.mergedPartyExpectedVersion)) {
    return Promise.resolve({ type: 'unfenced' })
  }
  return putVersionedCommand(
    mergePath(survivorPartyId),
    context,
    expectedVersion,
    {
      mergedPartyId: approval.mergedPartyId,
      mergedPartyExpectedVersion: approval.mergedPartyExpectedVersion,
      reason: approval.reason,
    },
    signal,
    MERGE_REPLAYABLE,
  )
}
