/**
 * ORG-05 (KIM-S23) — the two VKN change commands:
 *
 *   POST /api/v1/organization/company/tax-id/change-requests                  {expectedVersion, proposedVkn, reason} -> 201
 *   POST /api/v1/organization/company/tax-id/change-requests/{id}/decision    {expectedVersion, decision, note}      -> 204
 *
 * The wire rules are the master edit's (./masterDataApi.ts), with one difference: EVERY 409 and 422 on this surface
 * carries a stable `code` (`OrganizationProblemCodesV1.CompanyTaxId*`), so the refusal is classified by that code and
 * not by the presence of a `conflict` object alone. A 409/422 whose code we do not know is UNKNOWN, never a refusal
 * we invent a reason for. No response, a 5xx, another 2xx or an unreadable answer is UNKNOWN — the command may have
 * applied, and the caller re-sends with the SAME key. Nothing here parses a localized title or detail.
 */

import { API_BASE, devHeaders } from './api'
import type { CommandFailure, CommandUnknown } from './commandApi'
import { decodeCreatedRecord, type CreatedRecord } from './contract'
import {
  COMPANY_TAX_ID_PROBLEM,
  decodeCompanyTaxIdProblemCode,
  decodeTaxIdVersionConflict,
  type TaxIdDecision,
} from './companyTaxIdContract'

/** The refusals only this surface has; each needs its own text and recovery. */
export type TaxIdFailure =
  /** 422 — another company already carries the proposed VKN (DEC-ORG-005). */
  | { readonly kind: 'tax-id-in-use' }
  /** 422 — KIM-D40: the proposed VKN fails the official last-digit check; nothing was recorded for approval. */
  | { readonly kind: 'tax-id-check-digit-invalid' }
  /** 409 — the company already has an open change request; it must be decided first. */
  | { readonly kind: 'tax-id-change-pending' }
  /** 409 — the proposed VKN is the company's current one. */
  | { readonly kind: 'tax-id-unchanged' }
  /** 409 — the request was already approved or rejected. */
  | { readonly kind: 'tax-id-change-not-pending' }
  /** 409 — the company changed after the request was prepared; it can only be rejected. */
  | { readonly kind: 'tax-id-change-stale' }
  /** 409 — the preparer tried to decide their own request (hazırlayan ≠ onaylayan). */
  | { readonly kind: 'tax-id-self-decision' }
  /** 409 — KIM-D48: the staged correction was already applied. */
  | { readonly kind: 'tax-id-correction-not-pending' }
  /** 409 — KIM-D48: the company's VKN moved after the correction was staged; it can no longer be applied. */
  | { readonly kind: 'tax-id-correction-stale' }

export type CompanyTaxIdFailure = CommandFailure | TaxIdFailure

export type TaxIdRequestOutcome =
  | { readonly type: 'created'; readonly record: CreatedRecord; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly failure: CompanyTaxIdFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

export type TaxIdDecisionOutcome =
  | { readonly type: 'decided'; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly failure: CompanyTaxIdFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

export type TaxIdCorrectionApplyOutcome =
  | { readonly type: 'applied'; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly failure: CompanyTaxIdFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

export interface ApplyTaxIdCorrectionCommand {
  readonly correctionId: string
  /** The company version READ from the server. */
  readonly expectedVersion: number
}

export interface RequestTaxIdChangeCommand {
  /** The company version READ from the server. Never a predicted one. */
  readonly expectedVersion: number
  /** Raw user text; trimmed here so the wire value is the canonical one. */
  readonly proposedVkn: string
  readonly reason: string
}

export interface DecideTaxIdChangeCommand {
  readonly requestId: string
  /** The company version READ from the server. */
  readonly expectedVersion: number
  readonly decision: TaxIdDecision
  /** Optional; an empty note goes out as null. */
  readonly note: string
}

export const TAX_ID_REQUEST_PATH = '/organization/company/tax-id/change-requests'

export function taxIdDecisionPath(requestId: string): string {
  return `${TAX_ID_REQUEST_PATH}/${encodeURIComponent(requestId)}/decision`
}

export function taxIdCorrectionApplyPath(correctionId: string): string {
  return `/organization/company/tax-id/corrections/${encodeURIComponent(correctionId)}/apply`
}

/** The exact apply body: only the company version the approver saw. */
export function taxIdCorrectionApplyWireBody(command: ApplyTaxIdCorrectionCommand): Record<string, unknown> {
  return { expectedVersion: command.expectedVersion }
}

/** The exact request body: nothing the server does not take (no company, no current VKN, no next version). */
export function taxIdRequestWireBody(command: RequestTaxIdChangeCommand): Record<string, unknown> {
  return {
    expectedVersion: command.expectedVersion,
    proposedVkn: command.proposedVkn.trim(),
    reason: command.reason.trim(),
  }
}

export function taxIdDecisionWireBody(command: DecideTaxIdChangeCommand): Record<string, unknown> {
  const note = command.note.trim()
  return {
    expectedVersion: command.expectedVersion,
    decision: command.decision,
    note: note.length === 0 ? null : note,
  }
}

function classify(status: number, body: unknown, replayed: boolean): CompanyTaxIdFailure | null {
  if (status === 400) return { kind: 'validation' }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'unsupported-media' }
  if (status !== 409 && status !== 422) return null
  const code = decodeCompanyTaxIdProblemCode(body)
  if (status === 422) {
    if (code === COMPANY_TAX_ID_PROBLEM.inUse) return { kind: 'tax-id-in-use' }
    if (code === COMPANY_TAX_ID_PROBLEM.checkDigitInvalid) return { kind: 'tax-id-check-digit-invalid' }
    return null
  }
  switch (code) {
    case COMPANY_TAX_ID_PROBLEM.versionConflict: {
      const conflict = decodeTaxIdVersionConflict(body)
      return conflict === null ? null : { kind: 'version-conflict', conflict, conflictIsCurrent: !replayed }
    }
    case COMPANY_TAX_ID_PROBLEM.idempotencyConflict:
      return { kind: 'idempotency-conflict' }
    case COMPANY_TAX_ID_PROBLEM.changePending:
      return { kind: 'tax-id-change-pending' }
    case COMPANY_TAX_ID_PROBLEM.unchanged:
      return { kind: 'tax-id-unchanged' }
    case COMPANY_TAX_ID_PROBLEM.changeNotPending:
      return { kind: 'tax-id-change-not-pending' }
    case COMPANY_TAX_ID_PROBLEM.changeStale:
      return { kind: 'tax-id-change-stale' }
    case COMPANY_TAX_ID_PROBLEM.selfDecision:
      return { kind: 'tax-id-self-decision' }
    case COMPANY_TAX_ID_PROBLEM.correctionNotPending:
      return { kind: 'tax-id-correction-not-pending' }
    case COMPANY_TAX_ID_PROBLEM.correctionStale:
      return { kind: 'tax-id-correction-stale' }
    default:
      return null
  }
}

type Sent =
  | { readonly type: 'response'; readonly response: Response; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

async function post(
  path: string,
  companyId: string,
  idempotencyKey: string,
  body: Record<string, unknown>,
  signal?: AbortSignal,
): Promise<Sent> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(body),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', unknown: { kind: 'aborted' } }
    if (!navigator.onLine) return { type: 'unknown', unknown: { kind: 'offline' } }
    return { type: 'unknown', unknown: { kind: 'network-error' } }
  }
  const replayed = (response.headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'
  return { type: 'response', response, replayed }
}

/** Everything that is not the one success: UNKNOWN for 2xx/5xx/unreadable, otherwise a classified refusal. */
async function nonSuccess(
  response: Response,
  replayed: boolean,
): Promise<
  | { readonly type: 'failed'; readonly failure: CompanyTaxIdFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }
> {
  if (response.status >= 200 && response.status < 300) {
    return { type: 'unknown', unknown: { kind: 'unexpected-success', status: response.status } }
  }
  if (response.status >= 500) {
    return { type: 'unknown', unknown: { kind: 'service-unavailable', status: response.status } }
  }
  let parsed: unknown = null
  try {
    parsed = await response.json()
  } catch {
    parsed = null
  }
  const failure = classify(response.status, parsed, replayed)
  return failure === null
    ? { type: 'unknown', unknown: { kind: 'undecodable', status: response.status } }
    : { type: 'failed', failure, replayed }
}

/** Prepares a VKN change with the caller's key. Never retries on its own and never rotates the key. */
export async function sendCompanyTaxIdChangeRequest(
  companyId: string,
  idempotencyKey: string,
  command: RequestTaxIdChangeCommand,
  signal?: AbortSignal,
): Promise<TaxIdRequestOutcome> {
  const sent = await post(TAX_ID_REQUEST_PATH, companyId, idempotencyKey, taxIdRequestWireBody(command), signal)
  if (sent.type === 'unknown') return sent
  const { response, replayed } = sent
  if (response.status === 201) {
    let created: CreatedRecord | null = null
    try {
      created = decodeCreatedRecord(await response.json())
    } catch {
      created = null
    }
    // A 201 whose record we cannot read did create SOMETHING: that is unknown, never a refusal.
    return created === null
      ? { type: 'unknown', unknown: { kind: 'undecodable', status: 201 } }
      : { type: 'created', record: created, replayed }
  }
  return nonSuccess(response, replayed)
}

/** Approves or rejects another person's pending request with the caller's key. */
export async function sendCompanyTaxIdDecision(
  companyId: string,
  idempotencyKey: string,
  command: DecideTaxIdChangeCommand,
  signal?: AbortSignal,
): Promise<TaxIdDecisionOutcome> {
  const sent = await post(
    taxIdDecisionPath(command.requestId),
    companyId,
    idempotencyKey,
    taxIdDecisionWireBody(command),
    signal,
  )
  if (sent.type === 'unknown') return sent
  const { response, replayed } = sent
  if (response.status === 204) return { type: 'decided', replayed }
  return nonSuccess(response, replayed)
}

/**
 * KIM-D41/KIM-D48 — applies a staged VKN correction with the caller's key:
 *   POST /api/v1/organization/company/tax-id/corrections/{id}/apply   {expectedVersion} -> 204
 * The approver is recorded; the audit actor is the system source that staged it.
 */
export async function sendCompanyTaxIdCorrectionApply(
  companyId: string,
  idempotencyKey: string,
  command: ApplyTaxIdCorrectionCommand,
  signal?: AbortSignal,
): Promise<TaxIdCorrectionApplyOutcome> {
  const sent = await post(
    taxIdCorrectionApplyPath(command.correctionId),
    companyId,
    idempotencyKey,
    taxIdCorrectionApplyWireBody(command),
    signal,
  )
  if (sent.type === 'unknown') return sent
  const { response, replayed } = sent
  if (response.status === 204) return { type: 'applied', replayed }
  return nonSuccess(response, replayed)
}
