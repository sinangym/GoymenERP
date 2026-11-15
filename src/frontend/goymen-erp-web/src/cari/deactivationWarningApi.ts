/**
 * ALS-D99 / ALS-D100 transport — the Cari deactivation warning, the group preview and the group command.
 *
 * Same thin hand-written shape as ./legalNameApi.ts:
 *  - The READS are side reads of the Cari card: they send the client-contract declaration and the selected company and
 *    deliberately do NOT move the readiness slot (the card's own detail read owns it).
 *  - The group COMMAND shares the write gate, one Idempotency-Key per attempt and the "no acknowledgment = unknown
 *    outcome" rule through `postCommand`; its success is exactly 200 with a per-company outcome list.
 */

import { API_BASE, contractRequestHeader } from './clientContract'
import { postCommand, type CommandContext, type CommandOutcome, type ReplayableFailures } from './commandApi'
import {
  buildGroupDeactivationBody,
  decodeDeactivationWarningView,
  decodeGroupDeactivationPreview,
  decodeGroupDeactivationResult,
  deactivationWarningPath,
  groupDeactivationPath,
  groupDeactivationPreviewPath,
  type DeactivationWarningView,
  type GroupDeactivationPreview,
  type GroupDeactivationResult,
  type GroupDeactivationTarget,
} from './deactivationWarning'
import { problemCodeOf, type ProblemCode } from './contract'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export type SideReadOutcome<T> =
  | { readonly type: 'ok'; readonly data: T }
  | { readonly type: 'http'; readonly status: number; readonly code: ProblemCode | null }
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

async function sideRead<T>(
  path: string,
  companyId: string,
  decode: (raw: unknown) => T | null,
  signal?: AbortSignal,
): Promise<SideReadOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
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
    return { type: 'http', status: response.status, code: problemCodeOf(body) }
  }
  const data = decode(body)
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

/** The selected company's deactivation warning for the party's account (`GetCounterpartyDeactivationWarningV1`). */
export function fetchDeactivationWarning(
  partyId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<SideReadOutcome<DeactivationWarningView>> {
  return sideRead(
    deactivationWarningPath(partyId),
    companyId,
    (raw) => decodeDeactivationWarningView(raw, { partyId, companyId }),
    signal,
  )
}

/** Every reachable company's account with its warning (`GetCounterpartyGroupDeactivationPreviewV1`). */
export function fetchGroupDeactivationPreview(
  partyId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<SideReadOutcome<GroupDeactivationPreview>> {
  return sideRead(
    groupDeactivationPreviewPath(partyId),
    companyId,
    (raw) => decodeGroupDeactivationPreview(raw, { partyId }),
    signal,
  )
}

/**
 * `GroupDeactivateCounterpartyV1` stores no non-success answer of its own: each company's answer is stored under the
 * derived key and comes back inside the 200 list (`replayed` per company). A 4xx is therefore never a verified replay.
 */
export const GROUP_DEACTIVATION_REPLAYABLE: ReplayableFailures = new Map()

/**
 * Deactivate the party's account in each named company. Refused before a request exists (`validation` outcome is the
 * caller's) when the body cannot be built; the result decoder binds the answer to the companies that were named.
 */
export function groupDeactivateCounterparty(
  context: CommandContext,
  partyId: string,
  targets: readonly GroupDeactivationTarget[],
  canonicalReasonText: string,
  acknowledged: boolean,
  signal?: AbortSignal,
): Promise<CommandOutcome<GroupDeactivationResult>> | null {
  const body = buildGroupDeactivationBody(targets, canonicalReasonText, acknowledged)
  if (body === null) return null
  const requestedCompanyIds = body.targets.map((target) => target.companyId)
  return postCommand(
    groupDeactivationPath(partyId),
    context,
    body,
    (raw) => decodeGroupDeactivationResult(raw, { partyId, requestedCompanyIds }),
    GROUP_DEACTIVATION_REPLAYABLE,
    signal,
    200,
  )
}
