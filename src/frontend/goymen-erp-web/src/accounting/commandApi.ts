/**
 * The period commands (ACC-V0):
 *  - `POST /api/v1/accounting/periods` — propose `{periodId, startsOn, endsOn}`, success 201;
 *  - `POST /api/v1/accounting/periods/{id}/approval` — `{expectedVersion}`, success 200 (Ö4: never
 *    by the preparer; the server answers the preparer 403 whatever grants they hold);
 *  - `POST /api/v1/accounting/periods/{id}/close` — `{expectedVersion}`, success 200 (Ö5).
 *
 * Wire facts, from `AccountingEndpoints.cs`:
 *  - headers: `X-Company-Id` and one `Idempotency-Key`. The transport never mints or rotates the key
 *    — the caller owns it, one per frozen snapshot;
 *  - a replay (`Idempotency-Replayed: true` + `Idempotency-Original-Correlation-Id`) is the stored
 *    first answer, so the screen re-reads instead of trusting a command body for the current state;
 *  - every refusal carries a stable `code` except the non-disclosing 404; 403 is `forbidden` for
 *    every denial. The client branches on the CODE, never on a localized title; an undescribed code
 *    on 409 is not guessed;
 *  - a 5xx or no answer says nothing about our command: it is UNKNOWN, and re-sending the SAME
 *    snapshot is safe (ADR-011: the same key is the same command).
 */

import { ACCOUNTING_BASE, devHeaders } from './api'
import { periodCommandBody, type PeriodCommandSnapshot } from './contract'

/** A DEFINITE refusal: the command did not take effect, and the server said why. */
export type PeriodFailure =
  | { readonly kind: 'validation'; readonly field: string | null }
  | { readonly kind: 'unauthenticated' }
  /** 403 — not entitled, or (Ö4) the preparer asked to approve their own period. */
  | { readonly kind: 'forbidden' }
  /** 404 — no such period in this company. Non-disclosing. */
  | { readonly kind: 'not-found' }
  | {
      readonly kind: 'version-conflict'
      readonly expectedVersion: number | null
      readonly currentVersion: number | null
      /** A replayed conflict is the first attempt's history, not the current state. */
      readonly conflictIsCurrent: boolean
    }
  | { readonly kind: 'idempotency-conflict' }
  | { readonly kind: 'concurrent-change' }
  /** 409 overlap / same id with other dates / same id by another preparer. */
  | { readonly kind: 'period-conflict'; readonly code: string }
  /** 422 — a period rule refused it, by its stable code. */
  | { readonly kind: 'rule'; readonly code: string }
  | { readonly kind: 'unsupported-media' }

/** The outcome is UNKNOWN: the command may or may not have been applied. */
export type PeriodUnknown =
  | { readonly kind: 'offline' }
  | { readonly kind: 'network-error' }
  | { readonly kind: 'service-unavailable'; readonly status: number }
  | { readonly kind: 'unexpected-success'; readonly status: number }
  | { readonly kind: 'undecodable'; readonly status: number }
  | { readonly kind: 'aborted' }

export type PeriodCommandOutcome =
  | { readonly type: 'applied'; readonly replayed: boolean; readonly originalCorrelationId: string | null }
  | { readonly type: 'failed'; readonly failure: PeriodFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: PeriodUnknown }

const PERIOD_CONFLICT_CODES: ReadonlySet<string> = new Set([
  'accounting_period_overlap',
  'accounting_period_conflict',
  'accounting_period_preparer_conflict',
])

function readReplayed(headers: Headers): boolean {
  return (headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'
}

function readOriginalCorrelationId(headers: Headers): string | null {
  const value = headers.get('Idempotency-Original-Correlation-Id')
  return value !== null && value.trim().length > 0 ? value.trim() : null
}

function stringField(body: unknown, name: string): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const value = (body as Record<string, unknown>)[name]
  return typeof value === 'string' && value.length > 0 ? value : null
}

/** The server sends versions as invariant decimal strings in the problem extensions. */
function versionField(body: unknown, name: string): number | null {
  const text = stringField(body, name)
  if (text === null || !/^[1-9][0-9]{0,15}$/.test(text)) return null
  const value = Number(text)
  return Number.isSafeInteger(value) ? value : null
}

function classifyFailure(status: number, body: unknown, replayed: boolean): PeriodFailure | null {
  if (status === 400) return { kind: 'validation', field: stringField(body, 'field') }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'unsupported-media' }
  const code = stringField(body, 'code')
  if (code === null) return null
  if (status === 409) {
    if (code === 'idempotency_conflict') return { kind: 'idempotency-conflict' }
    if (code === 'accounting_version_conflict') {
      return {
        kind: 'version-conflict',
        expectedVersion: versionField(body, 'expectedVersion'),
        currentVersion: versionField(body, 'currentVersion'),
        conflictIsCurrent: !replayed,
      }
    }
    if (code === 'accounting_concurrent_change') return { kind: 'concurrent-change' }
    if (PERIOD_CONFLICT_CODES.has(code)) return { kind: 'period-conflict', code }
    return null
  }
  if (status === 422) return { kind: 'rule', code }
  return null
}

function url(snapshot: PeriodCommandSnapshot): string {
  const periods = `${ACCOUNTING_BASE}/periods`
  switch (snapshot.operation) {
    case 'propose':
      return periods
    case 'approve':
      return `${periods}/${encodeURIComponent(snapshot.periodId)}/approval`
    case 'close':
      return `${periods}/${encodeURIComponent(snapshot.periodId)}/close`
  }
}

/**
 * Sends one frozen period command. Returns a definite outcome or an explicitly UNKNOWN one; it
 * never guesses, never retries on its own and never changes the key or the period id.
 */
export async function sendPeriodCommand(
  companyId: string,
  snapshot: PeriodCommandSnapshot,
  signal?: AbortSignal,
): Promise<PeriodCommandOutcome> {
  let response: Response
  try {
    response = await fetch(url(snapshot), {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
        'Idempotency-Key': snapshot.idempotencyKey,
      },
      body: JSON.stringify(periodCommandBody(snapshot)),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', unknown: { kind: 'aborted' } }
    if (!navigator.onLine) return { type: 'unknown', unknown: { kind: 'offline' } }
    // For a WRITE, no response is NOT "it did not happen". The caller keeps the snapshot.
    return { type: 'unknown', unknown: { kind: 'network-error' } }
  }

  const replayed = readReplayed(response.headers)
  const success = snapshot.operation === 'propose' ? 201 : 200
  if (response.status === success) {
    return { type: 'applied', replayed, originalCorrelationId: readOriginalCorrelationId(response.headers) }
  }
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
  const failure = classifyFailure(response.status, parsed, replayed)
  if (failure === null) return { type: 'unknown', unknown: { kind: 'undecodable', status: response.status } }
  return { type: 'failed', failure, replayed }
}
