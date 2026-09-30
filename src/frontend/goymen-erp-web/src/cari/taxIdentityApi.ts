/**
 * AT-009 transport — `…/counterparties/{partyId}/tax-identity` (GET view, GET duplicates, PUT enter,
 * PUT change-request, PUT change-request/decision).
 *
 * Same thin hand-written shape as ./contactInformationApi.ts. The reads send the client-contract declaration and the
 * selected company, and deliberately do NOT move the command-readiness slot: the card's own detail read owns that,
 * and a failing side panel must not close (or open) the write gate for the account-status and contact commands.
 * The three commands go through `putVersionedCommand`, so they share its gate, its lossless PARTY-version fence, its
 * exact-204 rule and its "no acknowledgment = unknown outcome" rule.
 */

import { API_BASE, contractRequestHeader } from './clientContract'
import { putVersionedCommand, type CommandContext, type CommandOutcome, type ReplayableFailures } from './commandApi'
import {
  TAX_PROBLEM_CODE,
  decodeTaxDuplicateCheck,
  decodeTaxIdentityView,
  taxIdentityPath,
  taxProblemCodeOf,
  type TaxDuplicateCheck,
  type TaxIdentityView,
} from './taxIdentity'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export type TaxReadOutcome<T> =
  | { readonly type: 'ok'; readonly data: T }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

async function getJson<T>(
  path: string,
  companyId: string,
  decode: (raw: unknown) => T | null,
  signal?: AbortSignal,
): Promise<TaxReadOutcome<T>> {
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

  if (response.status !== 200) return { type: 'http', status: response.status, code: taxProblemCodeOf(body) }
  const data = decode(body)
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

export function fetchTaxIdentity(
  partyId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<TaxReadOutcome<TaxIdentityView>> {
  return getJson(taxIdentityPath(partyId), companyId, (raw) => decodeTaxIdentityView(raw, { partyId, companyId }), signal)
}

/** The duplicate warning for a candidate number, before it is saved. */
export function checkTaxIdentityDuplicates(
  partyId: string,
  companyId: string,
  number: string,
  signal?: AbortSignal,
): Promise<TaxReadOutcome<TaxDuplicateCheck>> {
  return getJson(
    `${taxIdentityPath(partyId)}/duplicates?number=${encodeURIComponent(number)}`,
    companyId,
    (raw) => decodeTaxDuplicateCheck(raw, number),
    signal,
  )
}

const replayable = (...codes: string[]): ReplayableFailures =>
  new Map([
    [404, new Set<string>([TAX_PROBLEM_CODE.notFound])],
    [409, new Set<string>([TAX_PROBLEM_CODE.versionConflict, ...codes])],
  ])

/** First entry; `expectedVersion` is the PARTY version of the read the form opened on. */
export function enterTaxIdentity(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  taxIdentityNumber: string,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    taxIdentityPath(partyId),
    context,
    expectedVersion,
    { taxIdentityNumber },
    signal,
    replayable(TAX_PROBLEM_CODE.requiresApproval),
  )
}

export function requestTaxIdentityChange(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  proposedTaxIdentityNumber: string,
  reason: string,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    `${taxIdentityPath(partyId)}/change-request`,
    context,
    expectedVersion,
    { proposedTaxIdentityNumber, reason },
    signal,
    replayable(TAX_PROBLEM_CODE.notSet, TAX_PROBLEM_CODE.unchanged, TAX_PROBLEM_CODE.changePending),
  )
}

export function decideTaxIdentityChange(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  changeRequestId: string,
  decision: 'approve' | 'reject',
  note: string | null,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    `${taxIdentityPath(partyId)}/change-request/decision`,
    context,
    expectedVersion,
    { changeRequestId, decision, note },
    signal,
    replayable(TAX_PROBLEM_CODE.changeNotPending, TAX_PROBLEM_CODE.changeStale, TAX_PROBLEM_CODE.selfDecision),
  )
}
