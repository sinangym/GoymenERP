/**
 * ACC-V1 journal reversal transport. One frozen snapshot per command: the caller owns the proposal id and the
 * `Idempotency-Key`; this module never mints, rotates or retries them (ADR-011: the same key is the same command).
 *
 * Outcomes:
 *  - `applied`  — exactly the route's success status (201 propose, 200 approve) with a decodable view of THIS
 *                 proposal in the bound company;
 *  - `failed`   — a definite refusal the server described by status and stable code (the command took no effect);
 *  - `unknown`  — no answer, a 5xx, an undescribed code or an undecodable success: the command MAY have applied,
 *                 so the screen offers only the same snapshot again.
 */

import type { ViewOutcome } from '../shell/uiState'
import { ACCOUNTING_BASE, devHeaders } from './api'
import {
  decodeJournalReversalProposal,
  sameGuid,
  type JournalReversalProposal,
  type ProposeJournalReversalRequest,
} from './journalReversalContract'

export const JOURNAL_REVERSAL_PROPOSALS = `${ACCOUNTING_BASE}/journal-reversal-proposals`

export type JournalReversalSnapshot =
  | { readonly operation: 'propose'; readonly idempotencyKey: string; readonly request: ProposeJournalReversalRequest }
  | {
      readonly operation: 'approve'
      readonly idempotencyKey: string
      readonly proposalId: string
      readonly expectedVersion: number
    }

/** Route-exact 422 codes (owner catalog `AccountingJournalReversalUnprocessableCodesV1`). */
export const PROPOSE_RULE_CODES: ReadonlySet<string> = new Set([
  'accounting_journal_reversal_source_journal_not_found',
  'accounting_journal_reversal_period_not_found',
])
// #485: a proposal's period is admitted by identity only, so at approval it may still be proposed-not-approved
// (`accounting_period_not_open`, 422 leaving the proposal Pending), closed or not covering the accounting date.
export const APPROVE_RULE_CODES: ReadonlySet<string> = new Set([
  'accounting_invalid_reference',
  'accounting_reversal_of_reversal',
  'accounting_period_closed',
  'accounting_period_does_not_cover_date',
  'accounting_period_not_open',
  'accounting_period_not_found',
])

export type JournalReversalFailure =
  | { readonly kind: 'validation'; readonly field: string | null }
  | { readonly kind: 'unauthenticated' }
  /** 403 — not entitled, or the proposer asked to approve their own proposal (maker ≠ checker). */
  | { readonly kind: 'forbidden' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'version-conflict'; readonly expectedVersion: number | null; readonly currentVersion: number | null }
  | { readonly kind: 'idempotency-conflict' }
  | { readonly kind: 'concurrent-change' }
  | { readonly kind: 'content-conflict' }
  | { readonly kind: 'not-pending' }
  | { readonly kind: 'posting-conflict' }
  | { readonly kind: 'rule'; readonly code: string }
  | { readonly kind: 'unsupported-media' }

export type JournalReversalUnknownReason =
  | 'offline'
  | 'network'
  | 'service'
  | 'malformed'
  | 'unexpected-success'
  | 'aborted'

export type JournalReversalOutcome =
  | { readonly type: 'applied'; readonly proposal: JournalReversalProposal; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly failure: JournalReversalFailure }
  | { readonly type: 'unknown'; readonly reason: JournalReversalUnknownReason }

function field(body: unknown, key: string): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const value = (body as Record<string, unknown>)[key]
  return typeof value === 'string' && value.length > 0 ? value : null
}

function versionField(body: unknown, key: string): number | null {
  const text = field(body, key)
  if (text === null || !/^[1-9][0-9]{0,15}$/.test(text)) return null
  const parsed = Number(text)
  return Number.isSafeInteger(parsed) ? parsed : null
}

export function classifyJournalReversalFailure(
  operation: JournalReversalSnapshot['operation'],
  status: number,
  body: unknown,
): JournalReversalFailure | null {
  if (status === 400) return { kind: 'validation', field: field(body, 'field') }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  // Propose has no 404 contract (an unknown source is a 422); only approval answers a code-less 404.
  if (status === 404) return operation === 'approve' ? { kind: 'not-found' } : null
  if (status === 415) return { kind: 'unsupported-media' }
  const code = field(body, 'code')
  if (code === null) return null
  if (status === 409) {
    switch (code) {
      case 'idempotency_conflict':
        return { kind: 'idempotency-conflict' }
      case 'accounting_concurrent_change':
        return { kind: 'concurrent-change' }
      case 'accounting_journal_reversal_proposal_content_conflict':
        return { kind: 'content-conflict' }
      case 'accounting_journal_reversal_not_pending':
        return { kind: 'not-pending' }
      case 'accounting_journal_reversal_posting_conflict':
        return { kind: 'posting-conflict' }
      case 'accounting_version_conflict':
      case 'accounting_journal_reversal_version_conflict':
        return {
          kind: 'version-conflict',
          expectedVersion: versionField(body, 'expectedVersion'),
          currentVersion: versionField(body, 'currentVersion'),
        }
      default:
        return null
    }
  }
  if (status === 422) {
    const known = operation === 'propose' ? PROPOSE_RULE_CODES : APPROVE_RULE_CODES
    return known.has(code) ? { kind: 'rule', code } : null
  }
  return null
}

export async function sendJournalReversalCommand(
  companyId: string,
  snapshot: JournalReversalSnapshot,
  signal?: AbortSignal,
): Promise<JournalReversalOutcome> {
  const propose = snapshot.operation === 'propose'
  const url = propose
    ? JOURNAL_REVERSAL_PROPOSALS
    : `${JOURNAL_REVERSAL_PROPOSALS}/${encodeURIComponent(snapshot.proposalId)}/approval`
  const body = propose ? snapshot.request : { expectedVersion: snapshot.expectedVersion }
  let response: Response
  try {
    response = await fetch(url, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
        'Idempotency-Key': snapshot.idempotencyKey,
      },
      body: JSON.stringify(body),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', reason: 'aborted' }
    // For a WRITE, no response is NOT "it did not happen": the caller keeps the snapshot.
    return { type: 'unknown', reason: navigator.onLine ? 'network' : 'offline' }
  }

  let parsed: unknown = null
  try {
    parsed = await response.json()
  } catch {
    parsed = null
  }
  const success = propose ? 201 : 200
  if (response.status === success) {
    const proposal = decodeJournalReversalProposal(parsed, companyId)
    const expectedId = propose ? snapshot.request.proposalId : snapshot.proposalId
    if (proposal === null || !sameGuid(proposal.proposalId, expectedId)) return { type: 'unknown', reason: 'malformed' }
    return {
      type: 'applied',
      proposal,
      replayed: (response.headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true',
    }
  }
  if (response.status >= 200 && response.status < 300) return { type: 'unknown', reason: 'unexpected-success' }
  if (response.status >= 500) return { type: 'unknown', reason: 'service' }
  const refused = classifyJournalReversalFailure(snapshot.operation, response.status, parsed)
  return refused === null ? { type: 'unknown', reason: 'malformed' } : { type: 'failed', failure: refused }
}

/**
 * `GET /accounting/journal-reversal-proposals/{id}` — the reopen read the approval screen stands on.
 * STAGED: the host does not publish this route yet (see the approval capability in
 * `AccountingShellCapabilityProvider`), so the approval screen stays closed until the Accounting owner's read
 * and its central route land. A success is EXACTLY 200 with a decodable proposal of the bound company.
 */
export async function fetchJournalReversalProposal(
  companyId: string,
  proposalId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<JournalReversalProposal>> {
  let response: Response
  try {
    response = await fetch(`${JOURNAL_REVERSAL_PROPOSALS}/${encodeURIComponent(proposalId)}`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const proposal = decodeJournalReversalProposal(body, companyId)
  return proposal === null || !sameGuid(proposal.proposalId, proposalId)
    ? { type: 'malformed' }
    : { type: 'ok-single', data: proposal }
}
