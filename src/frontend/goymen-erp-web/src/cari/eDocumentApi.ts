/**
 * SAT-D102 transport — the Cari account's e-belge profile.
 *
 * Same thin hand-written shape as ./customerGroupApi.ts, consuming the generated contract
 * (`contracts/openapi/goymen-erp-v1.json`, see ./eDocument.ts):
 *  - The READ is a side read of the Cari card: it sends the client-contract declaration and the selected company but
 *    deliberately does NOT move the card's readiness slot (the card's own detail read owns it).
 *  - COMMANDS go through `putVersionedCommand`: the write gate, one Idempotency-Key per attempt, the exact-204 rule,
 *    "no acknowledgment = unknown outcome", and the fence the read returned: the VKN-shared PROFILE version as decimal
 *    text ("0" = no profile yet, opted in via `allowZeroVersion`) plus the opaque profile token. A 503 (Settings could
 *    not be read; nothing stored) is an unknown outcome for the attempt model, so the same key is retried.
 */

import {
  API_BASE,
  beginContractRead,
  contractAckOf,
  contractRequestHeader,
  recordContractRead,
  recordContractReadFailure,
} from './clientContract'
import { putVersionedCommand, type CommandContext, type CommandOutcome, type ReplayableFailures } from './commandApi'
import {
  E_DOCUMENT_PROBLEM_CODE,
  decodeAccountEDocumentProfile,
  eDocumentPath,
  eDocumentProblemCodeOf,
  type AccountEDocumentProfile,
  type EDocumentStatus,
} from './eDocument'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export type EDocumentReadOutcome =
  | { readonly type: 'ok'; readonly data: AccountEDocumentProfile }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

/** The selected company's account of this party and its e-belge profile (a side read of the Cari card). */
export async function fetchAccountEDocumentProfile(
  partyId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<EDocumentReadOutcome> {
  beginContractRead(null)
  let response: Response
  try {
    response = await fetch(`${API_BASE}${eDocumentPath(partyId)}`, {
      headers: { ...devHeaders(), ...contractRequestHeader(), 'X-Company-Id': companyId },
      signal,
    })
  } catch (error) {
    if (signal?.aborted) {
      recordContractReadFailure(null, 'aborted')
      return { type: 'aborted' }
    }
    recordContractReadFailure(null, 'unreachable')
    if (!navigator.onLine) return { type: 'offline' }
    if (error instanceof TypeError) return { type: 'network-error' }
    throw error
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) {
      recordContractReadFailure(null, 'aborted')
      return { type: 'aborted' }
    }
    body = null
  }

  const ack = contractAckOf(response)
  if (response.status !== 200) {
    recordContractRead(null, { status: response.status, bodyValidated: false, ack })
    return { type: 'http', status: response.status, code: eDocumentProblemCodeOf(body) }
  }
  const data = decodeAccountEDocumentProfile(body, { partyId, companyId })
  recordContractRead(null, { status: response.status, bodyValidated: data !== null, ack })
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

const replayable = (entries: ReadonlyArray<readonly [number, readonly string[]]>): ReplayableFailures =>
  new Map(entries.map(([status, codes]) => [status, new Set<string>(codes)]))

/** `RecordCounterpartyAccountEDocumentStatusV1` stores 204, 404, 409 (version) and 422 (passive account / no VKN). */
const STATUS_REPLAYABLE = replayable([
  [404, [E_DOCUMENT_PROBLEM_CODE.notFound]],
  [409, [E_DOCUMENT_PROBLEM_CODE.versionConflict]],
  [422, [E_DOCUMENT_PROBLEM_CODE.accountInactive, E_DOCUMENT_PROBLEM_CODE.taxIdentityRequired]],
])

/**
 * `SetCounterpartyAccountEInvoiceScenarioV1` stores 204, 404, 409 (version) and 422 (no list / not listed / passive
 * account / no VKN).
 */
const SCENARIO_REPLAYABLE = replayable([
  [404, [E_DOCUMENT_PROBLEM_CODE.notFound]],
  [409, [E_DOCUMENT_PROBLEM_CODE.versionConflict]],
  [422, [
    E_DOCUMENT_PROBLEM_CODE.codeListMissing,
    E_DOCUMENT_PROBLEM_CODE.notListed,
    E_DOCUMENT_PROBLEM_CODE.accountInactive,
    E_DOCUMENT_PROBLEM_CODE.taxIdentityRequired,
  ]],
])

/**
 * Record the e-belge status a user checked. The fence is the pair the read returned: `expectedVersion` (the VKN-shared
 * PROFILE version, "0" when no profile exists yet) and `expectedProfileToken` (which profile the read showed — a VKN
 * change in between makes the server answer 409 instead of writing another taxpayer's record).
 */
export function recordAccountEDocumentStatus(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  expectedProfileToken: string,
  status: EDocumentStatus,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    `${eDocumentPath(partyId)}/status`,
    context,
    expectedVersion,
    { expectedProfileToken, status },
    signal,
    STATUS_REPLAYABLE,
    true,
  )
}

/**
 * Set a Settings-listed scenario code as the customer's override, or clear it with `null` (sent explicitly: the
 * contract requires the property).
 */
export function setAccountEInvoiceScenario(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  expectedProfileToken: string,
  scenarioCode: string | null,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    `${eDocumentPath(partyId)}/scenario`,
    context,
    expectedVersion,
    { expectedProfileToken, scenarioCode },
    signal,
    SCENARIO_REPLAYABLE,
    true,
  )
}
