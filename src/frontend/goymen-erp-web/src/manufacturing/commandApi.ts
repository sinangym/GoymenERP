/**
 * The bounded fact commands currently exposed by the order screen:
 * `input-issues` · `input-returns` · `wip-measurements` · owner-backed `outputs`.
 *
 * Wire facts, all from the merged MFG-API (#313) endpoint and its README:
 *
 *  - input issue/return/output use their strict Inventory realization request; WIP keeps
 *    `RecordFactRequestV1`. Output owner/custodian are frozen from the exact Inventory issue
 *    trace for this order; no body carries a predicted version;
 *  - headers: `X-Company-Id`, one `Idempotency-Key`. The transport never mints or rotates
 *    the key — the caller owns it, one per frozen snapshot;
 *  - success is EXACTLY 200 with the order view as it was when the command was first
 *    answered. A replay (`Idempotency-Replayed: true` + `Idempotency-Original-Correlation-Id`)
 *    repeats that stored answer verbatim, so it is history, never the current state;
 *  - every refusal carries a stable `code` except the non-disclosing 404. The client
 *    branches on the CODE, never on a localized title. 409 is FOUR answers told apart by
 *    it; 422 is a domain rule named by it. The declared set is a superset per endpoint
 *    (README "409 / 422 codes are one declared superset"): an unknown code is shown as a
 *    generic refusal with the code, not guessed;
 *  - a 5xx (for example 503 when the company's kilogram source is unavailable) says
 *    nothing about our command; it is UNKNOWN, and re-sending the SAME snapshot is safe:
 *    the server answers the same key as the same command (ADR-011) and the same fact id
 *    as the same fact (R3).
 */

import { closeBody, factBody, type CloseSnapshot, type FactSnapshot } from './contract'
import { MANUFACTURING_BASE, devHeaders } from './api'

/** A DEFINITE refusal: the command did not take effect, and the server said why. */
export type FactFailure =
  /** 400 — the request was malformed; `field` names the part when the server does. */
  | { readonly kind: 'validation'; readonly field: string | null }
  | { readonly kind: 'unauthenticated' }
  /** 403 — not entitled (or the company's grant is not fresh/company-wide). */
  | { readonly kind: 'forbidden' }
  /** 404 — no such order in this company. Non-disclosing. */
  | { readonly kind: 'not-found' }
  /**
   * 409 `manufacturing_version_conflict` — the order moved on. `currentVersion` is only
   * the order's current version when the answer is NOT a replay (a replayed conflict is
   * the first attempt's history); the screen re-reads either way.
   */
  | {
      readonly kind: 'version-conflict'
      readonly expectedVersion: number | null
      readonly currentVersion: number | null
      readonly conflictIsCurrent: boolean
    }
  /** 409 `idempotency_conflict` — this key already answered another request or actor. */
  | { readonly kind: 'idempotency-conflict' }
  /** 409 `manufacturing_concurrent_change` — another writer committed first; nothing stored. */
  | { readonly kind: 'concurrent-change' }
  /** 409 — Inventory's parent label moved after the fresh detail read. */
  | { readonly kind: 'label-version-conflict' }
  /** 409 `manufacturing_*_id_conflict` — this fact id already names other content. */
  | { readonly kind: 'id-conflict'; readonly code: string }
  /** 409/422 — a domain, Inventory or kilogram-profile rule refused it, by its stable code. */
  | { readonly kind: 'rule'; readonly code: string }
  | { readonly kind: 'unsupported-media' }

/** The outcome is UNKNOWN: the command may or may not have been applied. */
export type FactUnknown =
  | { readonly kind: 'offline' }
  | { readonly kind: 'network-error' }
  | { readonly kind: 'service-unavailable'; readonly status: number }
  | { readonly kind: 'unexpected-success'; readonly status: number }
  | { readonly kind: 'undecodable'; readonly status: number }
  | { readonly kind: 'aborted' }

export type FactOutcome =
  | {
      readonly type: 'applied'
      readonly replayed: boolean
      readonly originalCorrelationId: string | null
    }
  | { readonly type: 'failed'; readonly failure: FactFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: FactUnknown }

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

/**
 * A described status becomes a definite refusal; anything the contract does not
 * describe — including a 409/422 whose code never arrived — is null and becomes UNKNOWN,
 * because an answer we cannot read is not evidence that the command did not apply.
 */
function classifyFailure(status: number, body: unknown, replayed: boolean): FactFailure | null {
  if (status === 400) return { kind: 'validation', field: stringField(body, 'field') }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'unsupported-media' }
  const code = stringField(body, 'code')
  if (code === null) return null
  if (status === 409) {
    if (code === 'idempotency_conflict' || code === 'inventory_manufacturing_realization_idempotency_conflict') {
      return { kind: 'idempotency-conflict' }
    }
    if (code === 'manufacturing_version_conflict') {
      return {
        kind: 'version-conflict',
        expectedVersion: versionField(body, 'expectedVersion'),
        currentVersion: versionField(body, 'currentVersion'),
        conflictIsCurrent: !replayed,
      }
    }
    if (code === 'manufacturing_concurrent_change' || code === 'inventory_manufacturing_realization_concurrent_change') {
      return { kind: 'concurrent-change' }
    }
    if (code === 'inventory_manufacturing_parent_label_version_conflict') {
      return { kind: 'label-version-conflict' }
    }
    if (/^manufacturing_[a-z_]+_id_conflict$/.test(code)) return { kind: 'id-conflict', code }
    if (/^inventory_manufacturing_[a-z_]+$/.test(code)) return { kind: 'rule', code }
    return null
  }
  if (status === 422) return { kind: 'rule', code }
  return null
}

/**
 * Sends one frozen fact. Returns a definite outcome or an explicitly UNKNOWN one; it
 * never guesses, never retries on its own and never changes the key or the fact id.
 */
export function recordFact(
  companyId: string,
  orderId: string,
  snapshot: FactSnapshot,
  signal?: AbortSignal,
): Promise<FactOutcome> {
  return sendCommand(
    companyId,
    `${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}/${snapshot.kind}`,
    snapshot.idempotencyKey,
    factBody(snapshot),
    signal,
  )
}

/**
 * Closes one order (MFG-ORD): `POST …/orders/{orderId}/close` with the version the screen read.
 *
 * The same command discipline as a fact, and the same outcomes: 200 is the closed order, a
 * replay is the first close's stored answer (no second close), a 409 names the version conflict,
 * and a 422 names the domain's own refusal — `manufacturing_unexplained_difference` when a roll
 * does not account for its issued mass. Nothing here softens a refusal or retries on its own.
 */
export function closeOrder(
  companyId: string,
  orderId: string,
  snapshot: CloseSnapshot,
  signal?: AbortSignal,
): Promise<FactOutcome> {
  return sendCommand(
    companyId,
    `${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}/close`,
    snapshot.idempotencyKey,
    closeBody(snapshot),
    signal,
  )
}

async function sendCommand(
  companyId: string,
  url: string,
  idempotencyKey: string,
  body: Record<string, unknown>,
  signal?: AbortSignal,
): Promise<FactOutcome> {
  let response: Response
  try {
    response = await fetch(url, {
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
    // For a WRITE, no response is NOT "it did not happen". The caller keeps the snapshot.
    return { type: 'unknown', unknown: { kind: 'network-error' } }
  }

  const replayed = readReplayed(response.headers)
  if (response.status === 200) {
    return {
      type: 'applied',
      replayed,
      originalCorrelationId: readOriginalCorrelationId(response.headers),
    }
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
  if (failure === null) {
    return { type: 'unknown', unknown: { kind: 'undecodable', status: response.status } }
  }
  return { type: 'failed', failure, replayed }
}
